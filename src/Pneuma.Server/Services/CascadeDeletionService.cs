namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Models;
    using Pneuma.Core.Storage;

    /// <summary>
    /// Centralizes cascading deletion of subjects, links, and ingestion jobs and everything they produce,
    /// across the database and the external stores (S3 pipeline artifacts + raw blobs, the LiteGraph
    /// knowledge graph, and the RecallDB retrieval store). External-store cleanup is best-effort so an
    /// unavailable subordinate service never blocks removal of the authoritative database records; graph
    /// deletions preserve entity nodes that belong to other jobs/subjects.
    /// </summary>
    public class CascadeDeletionService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IArtifactStore _Artifacts;
        private readonly IVectorRepository _Vectors;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly IBlobStore _Blobs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the cascade deletion service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="artifacts">Per-stage S3 artifact store.</param>
        /// <param name="vectors">Vector repository (RecallDB) holding the job's chunk documents.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="blobs">Blob store.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public CascadeDeletionService(DatabaseDriverBase db, IArtifactStore artifacts, IVectorRepository vectors, IGraphRepositoryFactory graphFactory, IBlobStore blobs)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            _Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Delete a single ingestion job and its per-job contributions: graph nodes/edges it asserted
        /// (LiteGraph), its chunk documents (RecallDB), its raw blob, its processing-log events, and the
        /// job row. Per-link S3 artifacts are not touched here — they belong to the link.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="job">The job to delete.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task DeleteJobCascadeAsync(string tenantId, IngestionJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            await CleanupJobExternalsAsync(job, token).ConfigureAwait(false);

            // Job events and the job row are removed together so a failure can never leave one without the other.
            await _Db.IngestionJobs.DeleteWithEventsAsync(tenantId, job.Id, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a link and everything it produced: every ingestion job (with the per-job cascade above),
        /// the per-link S3 pipeline artifacts, and the link row.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="linkId">Link identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the link row was deleted.</returns>
        public async Task<bool> DeleteLinkCascadeAsync(string tenantId, string linkId, CancellationToken token = default)
        {
            List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateByLinkAsync(tenantId, linkId, token).ConfigureAwait(false);

            // Best-effort external cleanup for every job first; the database rows then come out atomically.
            List<string> jobIds = new List<string>();
            foreach (IngestionJob job in jobs)
            {
                await CleanupJobExternalsAsync(job, token).ConfigureAwait(false);
                jobIds.Add(job.Id);
            }
            await TryExternalAsync(() => _Artifacts.DeleteAllForLinkAsync(linkId, token)).ConfigureAwait(false);

            // The jobs, their events, and the link row are removed in one transaction.
            return await _Db.SubjectLinks.DeleteWithJobsAsync(tenantId, linkId, jobIds, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete a subject and every subordinate object: all of its links (each with the full link cascade),
        /// its entire knowledge-graph subgraph (LiteGraph, matched on the subjectId tag), and the subject row.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if the subject row was deleted.</returns>
        public async Task<bool> DeleteSubjectCascadeAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);

            // Gather every subordinate id and run best-effort external cleanup before the atomic database delete.
            List<string> linkIds = new List<string>();
            List<string> jobIds = new List<string>();
            foreach (SubjectLink link in links)
            {
                linkIds.Add(link.Id);
                List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateByLinkAsync(tenantId, link.Id, token).ConfigureAwait(false);
                foreach (IngestionJob job in jobs)
                {
                    await CleanupJobExternalsAsync(job, token).ConfigureAwait(false);
                    jobIds.Add(job.Id);
                }
                await TryExternalAsync(() => _Artifacts.DeleteAllForLinkAsync(link.Id, token)).ConfigureAwait(false);
            }

            IGraphRepository subjectGraph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);
            await TryExternalAsync(() => subjectGraph.DeleteBySubjectAsync(subjectId, token)).ConfigureAwait(false);

            // The subject, its links, its jobs, and all job events are removed in one transaction.
            return await _Db.Subjects.DeleteWithSubordinatesAsync(tenantId, subjectId, linkIds, jobIds, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task CleanupJobExternalsAsync(IngestionJob job, CancellationToken token)
        {
            // Best-effort removal of a job's contributions to the external stores; never block the cascade.
            IGraphRepository jobGraph = await _GraphFactory.ForTenantAsync(job.TenantId, token).ConfigureAwait(false);
            await TryExternalAsync(() => jobGraph.DeleteByJobAsync(job.Id, token)).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(job.CollectionId))
            {
                await TryExternalAsync(() => _Vectors.DeleteByTagAsync(job.TenantId, job.CollectionId!, "jobId", job.Id, token)).ConfigureAwait(false);
            }
            if (!String.IsNullOrEmpty(job.BlobKey))
            {
                await TryExternalAsync(() => _Blobs.DeleteAsync(job.BlobKey!, token)).ConfigureAwait(false);
            }
        }

        private static async Task TryExternalAsync(Func<Task> action)
        {
            // Best-effort cleanup of an external/subordinate store; never abort the cascade on failure.
            try
            {
                await action().ConfigureAwait(false);
            }
            catch
            {
                // Swallow — the authoritative database rows are still removed.
            }
        }

        #endregion
    }
}
