namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Database;
    using SyslogLogging;

    /// <summary>
    /// Keeps one live version per link. After a job indexes a link's new content, it removes what earlier jobs for the
    /// same link wrote: their chunks (RecallDB documents tagged with the job id) and their Source and Cell graph nodes.
    /// Entity nodes are shared across links and jobs (a later job may have reused one an earlier job created), so they
    /// are never deleted here. The same removal, scoped to one job, clears a failed attempt's partial output before a
    /// retry so retries are idempotent. Removal is best-effort: failures are returned, never thrown.
    /// </summary>
    public class VersionRetirementService
    {
        #region Public-Members

        /// <summary>Nodes deleted per graph search while clearing a job's Source and Cell nodes. Default 500; clamped to [10, 5000].</summary>
        public int NodeBatchSize
        {
            get { return _NodeBatchSize; }
            set { _NodeBatchSize = Math.Clamp(value, 10, 5000); }
        }

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly IVectorRepository _Vectors;
        private readonly LoggingModule _Logging;
        private int _NodeBatchSize = 500;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public VersionRetirementService(DatabaseDriverBase db, IGraphRepositoryFactory graphFactory, IVectorRepository vectors, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Remove the output of every other job of the current job's link, except jobs still queued or running (a
        /// concurrent job owns its own output).
        /// </summary>
        /// <param name="current">The job that just indexed the link's new version.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was removed and any failures.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="current"/> is null.</exception>
        public async Task<RetirementResult> RetireOlderVersionsAsync(IngestionJob current, CancellationToken token)
        {
            if (current == null) throw new ArgumentNullException(nameof(current));
            RetirementResult result = new RetirementResult();
            List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateByLinkAsync(current.TenantId, current.LinkId, token).ConfigureAwait(false);
            foreach (IngestionJob job in jobs)
            {
                if (String.Equals(job.Id, current.Id, StringComparison.Ordinal)) continue;
                if (job.Status == IngestionStatusEnum.Queued || job.Status == IngestionStatusEnum.Processing) continue;
                await RemoveAsync(job, result, token).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>Remove one job's own output (its chunks and its Source and Cell nodes).</summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>What was removed and any failures.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
        public async Task<RetirementResult> RemoveJobOutputAsync(IngestionJob job, CancellationToken token)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            RetirementResult result = new RetirementResult();
            await RemoveAsync(job, result, token).ConfigureAwait(false);
            return result;
        }

        #endregion

        #region Private-Methods

        private async Task RemoveAsync(IngestionJob job, RetirementResult result, CancellationToken token)
        {
            bool removedSomething = false;
            if (!String.IsNullOrEmpty(job.CollectionId))
            {
                try
                {
                    await _Vectors.DeleteByTagAsync(job.TenantId, job.CollectionId!, "jobId", job.Id, token).ConfigureAwait(false);
                    PneumaMetrics.RecordIngestionRetired("chunk_set", 1);
                    removedSomething = true;
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    result.Errors.Add("chunks of job " + job.Id + ": " + e.Message);
                }
            }

            try
            {
                IGraphRepository graph = await _GraphFactory.ForTenantAsync(job.TenantId, token).ConfigureAwait(false);
                int nodes = await DeleteJobNodesAsync(graph, job.Id, Ontology.NodeCell, token).ConfigureAwait(false);
                nodes += await DeleteJobNodesAsync(graph, job.Id, Ontology.NodeSource, token).ConfigureAwait(false);
                result.NodesDeleted += nodes;
                if (nodes > 0) PneumaMetrics.RecordIngestionRetired("node", nodes);
                removedSomething = true;
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                result.Errors.Add("graph nodes of job " + job.Id + ": " + e.Message);
            }

            if (removedSomething) result.JobsRetired++;
            if (result.Errors.Count > 0) _Logging.Warn("[VersionRetirementService] partial removal for job " + job.Id + ": " + String.Join("; ", result.Errors));
        }

        private async Task<int> DeleteJobNodesAsync(IGraphRepository graph, string jobId, string nodeType, CancellationToken token)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>
            {
                { Ontology.TagAssertedByJob, jobId },
                { Ontology.TagNodeType, nodeType }
            };

            // A node that survives its delete would come back in the next search; stop when a batch holds nothing new so
            // a store that ignores deletes cannot loop forever.
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            int deleted = 0;
            while (true)
            {
                token.ThrowIfCancellationRequested();
                List<GraphNode> batch = await graph.SearchNodesByTagsAsync(tags, NodeBatchSize, token).ConfigureAwait(false);
                bool anyNew = false;
                foreach (GraphNode node in batch)
                {
                    if (!seen.Add(node.Id)) continue;
                    anyNew = true;
                    await graph.DeleteNodeAsync(node.Id, token).ConfigureAwait(false);
                    deleted++;
                }

                if (!anyNew || batch.Count < NodeBatchSize) break;
            }

            return deleted;
        }

        #endregion
    }
}
