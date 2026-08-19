namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Settings;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Orchestrates a single ingestion job through its two phases — categorization (type detection, cell
    /// extraction, ontology classification into a candidate plan) and hydration (graph merge, embedding,
    /// indexing) — handling stage timing, telemetry, operator cancellation, and terminal state. The work
    /// each stage performs lives in <see cref="IngestionStages"/>; the recording of progress and state
    /// lives in <see cref="IngestionJournal"/>.
    /// </summary>
    public class IngestionProcessor
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IAtomizer _DocumentAtom;
        private readonly IBlobStore _Blobs;
        private readonly IArtifactStore _Artifacts;
        private readonly IContentFetcher _Fetcher;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;
        private readonly TelemetryService _Telemetry;
        private readonly IngestionJournal _Journal;
        private readonly IngestionStages _Stages;
        private readonly int _StageTimeoutSeconds;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the processor.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="documentAtom">DocumentAtom client.</param>
        /// <param name="partio">Partio client.</param>
        /// <param name="verbex">Verbex client.</param>
        /// <param name="graph">LiteGraph client.</param>
        /// <param name="vectors">Vector repository.</param>
        /// <param name="blobs">Blob store.</param>
        /// <param name="artifacts">Per-stage S3 artifact store.</param>
        /// <param name="fetcher">Source content fetcher.</param>
        /// <param name="cipher">Cipher for decrypting model-runner keys.</param>
        /// <param name="settings">Ingestion settings (per-stage timeout).</param>
        /// <param name="retrieval">Retrieval settings (inverted-index toggle).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="telemetry">Shared telemetry service for pipeline spans.</param>
        public IngestionProcessor(
            DatabaseDriverBase db,
            IAtomizer documentAtom,
            IPartioClient partio,
            IInvertedIndex verbex,
            IGraphRepository graph,
            IVectorRepository vectors,
            IBlobStore blobs,
            IArtifactStore artifacts,
            IContentFetcher fetcher,
            Aes256Cipher cipher,
            IngestionSettings settings,
            RetrievalSettings retrieval,
            LoggingModule logging,
            TelemetryService telemetry)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _DocumentAtom = documentAtom ?? throw new ArgumentNullException(nameof(documentAtom));
            _Blobs = blobs ?? throw new ArgumentNullException(nameof(blobs));
            _Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            _Fetcher = fetcher ?? throw new ArgumentNullException(nameof(fetcher));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (retrieval == null) throw new ArgumentNullException(nameof(retrieval));
            _StageTimeoutSeconds = settings.StageTimeoutSeconds;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _Journal = new IngestionJournal(db, logging);
            _Stages = new IngestionStages(db, partio, verbex, graph, vectors, artifacts, _Journal, retrieval.UseInvertedIndex, logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Process a claimed job end-to-end, updating its status and the originating link.
        /// </summary>
        /// <param name="job">The claimed job (already marked Processing).</param>
        /// <param name="token">Cancellation token.</param>
        public async Task ProcessAsync(IngestionJob job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            using (RadiantSpan? jobSpan = _Telemetry.StartSpan("ingestion " + job.SourceUrl, SpanKindEnum.Consumer))
            {
                jobSpan?.SetTag("pneuma.job.id", job.Id);
                jobSpan?.SetTag("pneuma.tenant.id", job.TenantId);
                jobSpan?.SetTag("pneuma.subject.id", job.SubjectId);
                jobSpan?.SetTag("pneuma.source.url", job.SourceUrl);
                PneumaMetrics.RecordIngestionJob("started");

                try
                {
                    await _Journal.RecordEventAsync(job, IngestionStageEnum.Pending, IngestionStatusEnum.Processing,
                        "Ingestion started for " + job.SourceUrl + ".", 0, token).ConfigureAwait(false);

                    // Stage 1 — Categorization: fetch, atomize, and classify into a candidate plan.
                    CategorizationResult? categorization = await CategorizeAsync(job, jobSpan, token).ConfigureAwait(false);
                    if (categorization == null) return; // a categorization failure was already recorded

                    // Stage 2 — Hydration: commit the (auto-approved) candidate plan. There is no manual
                    // approval gate; categorization flows straight into hydration.
                    await HydrateAsync(job, categorization, token).ConfigureAwait(false);
                    jobSpan?.SetOk(null);
                }
                catch (JobCancelledException)
                {
                    // Operator pressed "Stop": the job is already marked Cancelled; record it and finish cleanly.
                    _Logging.Info("[IngestionProcessor] job " + job.Id + " cancelled by operator.");
                    PneumaMetrics.RecordIngestionJob("cancelled");
                    jobSpan?.SetError("Cancelled by operator.");
                    await _Journal.RecordEventAsync(job, job.Stage, IngestionStatusEnum.Cancelled, "Ingestion stopped by operator.", 0, token).ConfigureAwait(false);
                    await _Journal.UpdateLinkAsync(job, SubjectLinkStatusEnum.Failed, "Cancelled by operator.", token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Server shutdown — let the worker observe cancellation without marking the job failed.
                    throw;
                }
                catch (OperationCanceledException)
                {
                    string message = "Stage '" + job.Stage + "' timed out after " + _StageTimeoutSeconds + " seconds.";
                    _Logging.Warn("[IngestionProcessor] job " + job.Id + " " + message);
                    jobSpan?.SetError(message);
                    await _Journal.FailAsync(job, job.Stage, message, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn("[IngestionProcessor] job " + job.Id + " failed: " + e.Message);
                    jobSpan?.RecordException(e, true);
                    jobSpan?.SetError(e.Message);
                    await _Journal.FailAsync(job, job.Stage, e.Message, token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private async Task<CategorizationResult?> CategorizeAsync(IngestionJob job, RadiantSpan? jobSpan, CancellationToken token)
        {
            byte[] data = await DownloadAsync(job, token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("source", () => _Artifacts.PutSourceAsync(job.LinkId, data, null, token), token).ConfigureAwait(false);

            TypeDetectResult detected = await RunStageAsync(job, IngestionStageEnum.TypeDetection,
                stageToken => _DocumentAtom.DetectTypeAsync(data, stageToken),
                result => "Type detection complete — detected document type: " + result.Type + " (" + result.MimeType + ").",
                token).ConfigureAwait(false);
            if (detected.IsUnknown)
            {
                jobSpan?.SetError("Unknown or unsupported document type.");
                await _Journal.FailAsync(job, IngestionStageEnum.TypeDetection, "Unknown or unsupported document type.", token).ConfigureAwait(false);
                return null;
            }
            job.DocumentType = detected.Type;

            List<ExtractedCell> cells = await RunStageAsync(job, IngestionStageEnum.CellExtraction,
                stageToken => _DocumentAtom.ExtractCellsAsync(detected.Type, data, stageToken),
                result => "Semantic cell extraction complete — extracted " + result.Count + " cell(s).",
                token).ConfigureAwait(false);
            job.BlobKey = await _Blobs.WriteAsync(job.Id, data, token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("atoms", () => _Artifacts.PutAtomsAsync(job.LinkId, Json.Serialize(cells), token), token).ConfigureAwait(false);
            if (cells.Count == 0)
            {
                jobSpan?.SetError("No semantic cells extracted.");
                await _Journal.FailAsync(job, IngestionStageEnum.CellExtraction, "No semantic cells extracted.", token).ConfigureAwait(false);
                return null;
            }

            Subject? subject = await _Db.Subjects.ReadByIdAsync(job.SubjectId, token).ConfigureAwait(false);
            string subjectName = subject?.DisplayName ?? "Unknown subject";

            CandidateSubgraph subgraph = await RunStageAsync(job, IngestionStageEnum.Classification,
                stageToken => _Stages.ClassifyAsync(job, cells, subjectName, stageToken),
                result => "Ontology / knowledge-graph mapping complete — proposed " + result.Nodes.Count + " node(s) and " + result.Edges.Count + " relationship(s).",
                token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("subgraph", () => _Artifacts.PutSubgraphAsync(job.LinkId, Json.Serialize(subgraph), token), token).ConfigureAwait(false);

            string provenance = await _Stages.BuildPromptProvenanceAsync(job, token).ConfigureAwait(false);

            // End of the categorization phase: the candidate plan (proposed subgraph) is persisted and, under
            // auto-approval, flows straight into hydration. Emitted as Completed (not Processing) so the phase
            // reads as finished; the prompt provenance is folded in rather than logged as a separate row.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Categorization, IngestionStatusEnum.Completed,
                "Categorization complete — candidate plan proposes " + subgraph.Nodes.Count + " node(s) and " + subgraph.Edges.Count +
                " relationship(s); auto-approved, proceeding to hydration. Prompt provenance (for reproducibility): " + provenance + ".",
                0, token).ConfigureAwait(false);

            return new CategorizationResult { Cells = cells, Subgraph = subgraph };
        }

        private async Task HydrateAsync(IngestionJob job, CategorizationResult categorization, CancellationToken token)
        {
            // Start of the hydration phase: commit the approved plan to the graph, embeddings, and index.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Processing,
                "Hydration started — committing the candidate plan to the knowledge graph and search index.",
                0, token).ConfigureAwait(false);

            MergeResult merge = await RunStageAsync(job, IngestionStageEnum.GraphMerge,
                stageToken => _Stages.MergeAsync(job, categorization.Subgraph, stageToken),
                result => "Knowledge-graph insertion complete — inserted/linked " + result.NodeIds.Count + " node(s) and " + result.EdgeIds.Count + " edge(s).",
                token).ConfigureAwait(false);
            job.GraphNodeIds = merge.NodeIds;

            List<PartioChunk> chunks = await RunStageAsync(job, IngestionStageEnum.Embedding,
                stageToken => _Stages.EmbedAsync(job, categorization.Cells, stageToken),
                result => "Embedding generation complete — produced " + IngestionStages.CountEmbeddings(result) + " embedding vector(s) across " + result.Count + " chunk(s).",
                token).ConfigureAwait(false);
            await _Stages.PersistChunkArtifactsAsync(job, chunks, token).ConfigureAwait(false);

            List<string> verbexIds = await RunStageAsync(job, IngestionStageEnum.Indexing,
                stageToken => _Stages.IndexAsync(job, merge, chunks, stageToken),
                result => "Search indexing complete — indexed " + result.Count + " document(s), each linked back to its knowledge-graph node.",
                token).ConfigureAwait(false);
            job.VerbexDocumentIds = verbexIds;

            // Close out the hydration phase so it does not linger as "Processing" after its sub-stages finish;
            // its "Hydration started" marker now has a matching completion before the job itself completes.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Completed,
                "Hydration complete — knowledge graph and search index updated.",
                0, token).ConfigureAwait(false);

            await _Journal.CompleteAsync(job, token).ConfigureAwait(false);
        }

        private async Task<byte[]> DownloadAsync(IngestionJob job, CancellationToken token)
        {
            return await _Fetcher.FetchAsync(job.SourceUrl, token).ConfigureAwait(false);
        }

        private async Task<T> RunStageAsync<T>(
            IngestionJob job,
            IngestionStageEnum stage,
            Func<CancellationToken, Task<T>> action,
            Func<T, string> message,
            CancellationToken token)
        {
            // Honor an operator "Stop": if the job was cancelled out-of-band, abort before the next stage.
            IngestionJob? current = await _Db.IngestionJobs.ReadAsync(job.TenantId, job.Id, token).ConfigureAwait(false);
            if (current != null && current.Status == IngestionStatusEnum.Cancelled) throw new JobCancelledException();

            job.Stage = stage;
            await _Journal.UpdateJobAsync(job, token).ConfigureAwait(false);

            using (RadiantSpan? span = _Telemetry.StartSpan("stage:" + stage, SpanKindEnum.Internal))
            {
                span?.SetTag("pneuma.stage", stage.ToString());
                span?.SetTag("pneuma.job.id", job.Id);

                Stopwatch sw = Stopwatch.StartNew();
                try
                {
                    using (CancellationTokenSource stageCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        stageCts.CancelAfter(TimeSpan.FromSeconds(_StageTimeoutSeconds));
                        T result = await action(stageCts.Token).ConfigureAwait(false);
                        sw.Stop();
                        PneumaMetrics.RecordIngestionStage(stage.ToString(), "ok", sw.Elapsed.TotalSeconds);
                        span?.SetOk(null);
                        await _Journal.RecordEventAsync(job, stage, IngestionStatusEnum.Completed, message(result), sw.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);
                        return result;
                    }
                }
                catch (Exception e)
                {
                    sw.Stop();
                    PneumaMetrics.RecordIngestionStage(stage.ToString(), "failed", sw.Elapsed.TotalSeconds);
                    span?.RecordException(e, true);
                    span?.SetError(e.Message);
                    throw;
                }
            }
        }

        #endregion

        #region Nested-Types

        /// <summary>Raised internally when an operator cancels a job mid-flight so the pipeline stops without failing.</summary>
        private sealed class JobCancelledException : Exception
        {
        }

        #endregion
    }
}
