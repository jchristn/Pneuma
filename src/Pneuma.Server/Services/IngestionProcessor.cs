namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Caching;
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
        private readonly int _MaxAttempts;
        private readonly int _RetryBackoffBaseMs;
        private readonly int _RetryBackoffMaxMs;
        private readonly Dictionary<IngestionStageEnum, SemaphoreSlim> _StageGates;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the processor.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="documentAtom">DocumentAtom client.</param>
        /// <param name="processor">Semantic processor (chunk/embed/summarize).</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository (RecallDB) that stores chunk content + embeddings.</param>
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
            ISemanticProcessor processor,
            IGraphRepositoryFactory graphFactory,
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
            _MaxAttempts = settings.MaxAttempts;
            _RetryBackoffBaseMs = settings.RetryBackoffBaseMs;
            _RetryBackoffMaxMs = settings.RetryBackoffMaxMs;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _Journal = new IngestionJournal(db, logging);
            // One process-wide embedding cache (a global system size limit) shared by every job this worker runs.
            EmbeddingCache embeddingCache = new EmbeddingCache(settings.EmbeddingCacheSize);
            _Stages = new IngestionStages(db, processor, cipher, graphFactory, vectors, artifacts, _Journal, embeddingCache, logging);
            _StageGates = BuildStageGates(settings.StageConcurrency);
        }

        private static Dictionary<IngestionStageEnum, SemaphoreSlim> BuildStageGates(IngestionStageConcurrencySettings c)
        {
            return new Dictionary<IngestionStageEnum, SemaphoreSlim>
            {
                { IngestionStageEnum.ContentRetrieval, new SemaphoreSlim(c.ContentRetrieval, c.ContentRetrieval) },
                { IngestionStageEnum.TypeDetection, new SemaphoreSlim(c.TypeDetection, c.TypeDetection) },
                { IngestionStageEnum.CellExtraction, new SemaphoreSlim(c.CellExtraction, c.CellExtraction) },
                { IngestionStageEnum.Classification, new SemaphoreSlim(c.Classification, c.Classification) },
                { IngestionStageEnum.GraphMerge, new SemaphoreSlim(c.GraphMerge, c.GraphMerge) },
                { IngestionStageEnum.Summarization, new SemaphoreSlim(c.Summarization, c.Summarization) },
                { IngestionStageEnum.Chunking, new SemaphoreSlim(c.Chunking, c.Chunking) },
                { IngestionStageEnum.Embedding, new SemaphoreSlim(c.Embedding, c.Embedding) },
                { IngestionStageEnum.Indexing, new SemaphoreSlim(c.Indexing, c.Indexing) }
            };
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

                await _Journal.RecordEventAsync(job, IngestionStageEnum.Pending, IngestionStatusEnum.Processing,
                    "Ingestion started for " + job.SourceUrl + ".", 0, token).ConfigureAwait(false);

                // Each claim gets up to MaxAttempts inline attempts: a transient failure (a stage timeout or an
                // exception from a subordinate service) is retried after an exponential backoff rather than
                // failing the job outright. Deterministic hard fails (unknown type, no cells, unchanged content)
                // return a null categorization and are never retried; an operator "Stop" and server shutdown are
                // handled distinctly and never retried.
                int attempt = 0;
                while (true)
                {
                    attempt++;

                    // A fresh attempt records its own stage events, so clear any in-place failure marker left by
                    // a contended stage on the previous attempt.
                    job.StageFailureRecorded = false;

                    try
                    {
                        // Stage 1 — Categorization: fetch, atomize, and classify into a candidate plan.
                        CategorizationResult? categorization = await CategorizeAsync(job, jobSpan, token).ConfigureAwait(false);
                        if (categorization == null) return; // a hard fail (or delta skip) was already recorded

                        // Stage 2 — Hydration: commit the (auto-approved) candidate plan. There is no manual
                        // approval gate; categorization flows straight into hydration.
                        await HydrateAsync(job, categorization, token).ConfigureAwait(false);
                        jobSpan?.SetOk(null);
                        return;
                    }
                    catch (JobCancelledException)
                    {
                        // Operator pressed "Stop": the job is already marked Cancelled; record it and finish cleanly.
                        _Logging.Info("[IngestionProcessor] job " + job.Id + " cancelled by operator.");
                        PneumaMetrics.RecordIngestionJob("cancelled");
                        jobSpan?.SetError("Cancelled by operator.");
                        await _Journal.RecordEventAsync(job, job.Stage, IngestionStatusEnum.Cancelled, "Ingestion stopped by operator.", 0, token).ConfigureAwait(false);
                        await _Journal.UpdateLinkAsync(job, SubjectLinkStatusEnum.Failed, "Cancelled by operator.", token).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Server shutdown — let the worker observe cancellation without marking the job failed.
                        throw;
                    }
                    catch (OperationCanceledException)
                    {
                        // A per-stage timeout is treated as transient and retried.
                        string message = "Stage '" + job.Stage + "' timed out after " + _StageTimeoutSeconds + " seconds.";
                        if (await TryScheduleRetryAsync(job, attempt, message, token).ConfigureAwait(false)) continue;
                        _Logging.Warn("[IngestionProcessor] job " + job.Id + " " + message);
                        jobSpan?.SetError(message);
                        await _Journal.FailAsync(job, job.Stage, message, token).ConfigureAwait(false);
                        return;
                    }
                    catch (Exception e)
                    {
                        // Server shutdown surfacing as a generic exception must not mark the job failed.
                        if (token.IsCancellationRequested) throw;
                        if (await TryScheduleRetryAsync(job, attempt, e.Message, token).ConfigureAwait(false)) continue;
                        _Logging.Warn("[IngestionProcessor] job " + job.Id + " failed: " + e.Message);
                        jobSpan?.RecordException(e, true);
                        jobSpan?.SetError(e.Message);
                        await _Journal.FailAsync(job, job.Stage, e.Message, token).ConfigureAwait(false);
                        return;
                    }
                }
            }
        }

        #endregion

        #region Private-Methods

        private async Task<CategorizationResult?> CategorizeAsync(IngestionJob job, RadiantSpan? jobSpan, CancellationToken token)
        {
            byte[] data = await RunStageAsync(job, IngestionStageEnum.ContentRetrieval,
                stageToken => DownloadAsync(job, stageToken),
                result => "Content retrieval complete — fetched " + result.Length + " byte(s) from " + job.SourceUrl + ".",
                token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("source", () => _Artifacts.PutSourceAsync(job.LinkId, data, null, token), token).ConfigureAwait(false);

            // Delta detection: hash the fetched bytes and, if they are identical to what the link last ingested
            // successfully, skip the expensive type-detect / extract / classify / merge / embed / index work and
            // complete immediately. Re-processing identical content is deterministic, so skipping is safe and
            // saves the LLM + embedding cost. A previously-failed link has no stored hash and always re-processes.
            string contentHash = ComputeContentHash(data);
            SubjectLink? existingLink = await _Db.SubjectLinks.ReadAsync(job.TenantId, job.LinkId, token).ConfigureAwait(false);
            if (existingLink != null
                && existingLink.Status == SubjectLinkStatusEnum.Ingested
                && !String.IsNullOrEmpty(existingLink.ContentHash)
                && String.Equals(existingLink.ContentHash, contentHash, StringComparison.Ordinal))
            {
                await _Journal.RecordEventAsync(job, IngestionStageEnum.Categorization, IngestionStatusEnum.Completed,
                    "Source content is unchanged since the last successful ingestion (matching content hash) — skipping re-processing.",
                    0, token).ConfigureAwait(false);
                await _Journal.CompleteAsync(job, token, contentHash).ConfigureAwait(false);
                jobSpan?.SetOk(null);
                return null;
            }

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

            return new CategorizationResult { Cells = cells, Subgraph = subgraph, ContentHash = contentHash };
        }

        private async Task HydrateAsync(IngestionJob job, CategorizationResult categorization, CancellationToken token)
        {
            // Start of the hydration phase: commit the approved plan to the graph, embeddings, and index.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Processing,
                "Hydration started — committing the candidate plan to the knowledge graph and search index.",
                0, token).ConfigureAwait(false);

            MergeResult merge = await RunStageAsync(job, IngestionStageEnum.GraphMerge,
                stageToken => _Stages.MergeAsync(job, categorization.Subgraph, categorization.Cells, stageToken),
                result => "Knowledge-graph insertion complete — inserted/linked " + result.NodeIds.Count + " node(s) and " + result.EdgeIds.Count + " edge(s), including a cell node per extracted cell.",
                token).ConfigureAwait(false);
            job.GraphNodeIds = merge.NodeIds;

            // Summarization, chunking, and embedding run as three discrete, independently-timed stages.
            List<CellSummary> summaries = await RunStageAsync(job, IngestionStageEnum.Summarization,
                stageToken => _Stages.SummarizeCellsAsync(job, categorization.Cells, merge.CellNodeIds, stageToken),
                result => "Summarization complete — produced " + result.Count + " summary(ies) from " + categorization.Cells.Count + " cell(s).",
                token).ConfigureAwait(false);

            List<SemanticChunk> chunks = await RunStageAsync(job, IngestionStageEnum.Chunking,
                stageToken => _Stages.ChunkCellsAsync(job, categorization.Cells, merge.CellNodeIds, summaries, stageToken),
                result => "Chunking complete — produced " + result.Count + " chunk(s) from " + categorization.Cells.Count + " cell(s) and " + summaries.Count + " summary(ies).",
                token).ConfigureAwait(false);

            List<SemanticChunk> embeddedChunks = await RunStageAsync(job, IngestionStageEnum.Embedding,
                stageToken => _Stages.EmbedChunksAsync(job, chunks, stageToken),
                result => "Embedding complete — produced " + IngestionStages.CountEmbeddings(result) + " embedding vector(s) across " + result.Count + " chunk(s).",
                token).ConfigureAwait(false);
            await _Stages.PersistChunkArtifactsAsync(job, embeddedChunks, token).ConfigureAwait(false);

            await RunStageAsync(job, IngestionStageEnum.Indexing,
                stageToken => _Stages.IndexAsync(job, merge, embeddedChunks, stageToken),
                result => "Search indexing complete — stored " + result + " chunk document(s) in collection " + job.CollectionId + ", each linked back to its knowledge-graph node.",
                token).ConfigureAwait(false);

            // Close out the hydration phase so it does not linger as "Processing" after its sub-stages finish;
            // its "Hydration started" marker now has a matching completion before the job itself completes.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Completed,
                "Hydration complete — knowledge graph and search index updated.",
                0, token).ConfigureAwait(false);

            await _Journal.CompleteAsync(job, token, categorization.ContentHash).ConfigureAwait(false);
        }

        /// <summary>Compute the hex SHA-256 of the fetched source bytes, used for re-ingestion delta detection.</summary>
        /// <param name="data">The fetched source bytes.</param>
        /// <returns>A lowercase hex SHA-256 string.</returns>
        private static string ComputeContentHash(byte[] data)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data ?? Array.Empty<byte>());
                System.Text.StringBuilder builder = new System.Text.StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }

        private async Task<byte[]> DownloadAsync(IngestionJob job, CancellationToken token)
        {
            return await _Fetcher.FetchAsync(job.SourceUrl, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Decide whether a transiently-failed attempt should be retried and, if so, back off before the next
        /// one. Returns true when the caller should retry (having waited the backoff), false when attempts are
        /// exhausted and the job should fail. Bumps the persisted attempt count and records a "retrying" event
        /// so the contention is visible in the follow-logs. Propagates cancellation on server shutdown.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="attempt">The 1-based attempt number that just failed.</param>
        /// <param name="reason">The failure reason to surface in the retry event.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True to retry; false to give up.</returns>
        private async Task<bool> TryScheduleRetryAsync(IngestionJob job, int attempt, string reason, CancellationToken token)
        {
            if (attempt >= _MaxAttempts) return false;

            int delayMs = ComputeBackoffMs(attempt);
            job.AttemptCount = job.AttemptCount + 1;
            await _Journal.UpdateJobAsync(job, token).ConfigureAwait(false);
            await _Journal.RecordEventAsync(job, job.Stage, IngestionStatusEnum.Processing,
                "Attempt " + attempt.ToString(CultureInfo.InvariantCulture) + " of " + _MaxAttempts.ToString(CultureInfo.InvariantCulture) +
                " failed (" + reason + "); retrying in " + (delayMs / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "s.",
                0, token).ConfigureAwait(false);

            // Any in-place failure marker from a contended stage on this attempt is cleared at the top of the
            // next loop iteration; the backoff delay observes shutdown by throwing (handled by the caller).
            if (delayMs > 0) await Task.Delay(delayMs, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>Exponential backoff for the given attempt: base·2^(attempt-1), capped at the configured maximum.</summary>
        /// <param name="attempt">The 1-based attempt number that just failed.</param>
        /// <returns>The backoff delay in milliseconds.</returns>
        private int ComputeBackoffMs(int attempt)
        {
            if (_RetryBackoffBaseMs <= 0) return 0;
            double scaled = _RetryBackoffBaseMs * Math.Pow(2, attempt - 1);
            if (scaled > _RetryBackoffMaxMs) scaled = _RetryBackoffMaxMs;
            if (scaled > Int32.MaxValue) scaled = Int32.MaxValue;
            return (int)scaled;
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

            // Per-stage concurrency gate: bound how many jobs run this stage at once (independent of how many
            // jobs run overall) so a large enqueue cannot overwhelm the model runners / backends. Acquired
            // outside the stage timeout so time spent waiting for a slot is not charged against the timeout.
            // When no slot is immediately free, surface a friendly "waiting" event so the follow-logs make the
            // contention visible rather than looking stalled.
            SemaphoreSlim? gate = _StageGates.TryGetValue(stage, out SemaphoreSlim? resolved) ? resolved : null;
            IngestionJobEvent? queuedEvent = null;
            double queueMs = 0;
            if (gate != null && !gate.Wait(0))
            {
                Stopwatch queueSw = Stopwatch.StartNew();
                queuedEvent = await _Journal.RecordEventAsync(job, stage, IngestionStatusEnum.Queued,
                    "Waiting for a free slot at this step — other documents are being processed. It will start automatically once one frees up.",
                    0, token).ConfigureAwait(false);
                await gate.WaitAsync(token).ConfigureAwait(false);
                queueSw.Stop();
                queueMs = queueSw.Elapsed.TotalMilliseconds;
            }
            try
            {
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

                            // If the stage was contended, update its queued entry in place (carrying the wait
                            // time) rather than appending a second row for the same stage.
                            if (queuedEvent != null)
                            {
                                await _Journal.ResolveEventAsync(queuedEvent, IngestionStatusEnum.Completed, message(result), sw.Elapsed.TotalMilliseconds, queueMs, token).ConfigureAwait(false);
                            }
                            else
                            {
                                await _Journal.RecordEventAsync(job, stage, IngestionStatusEnum.Completed, message(result), sw.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);
                            }
                            return result;
                        }
                    }
                    catch (Exception e)
                    {
                        sw.Stop();
                        PneumaMetrics.RecordIngestionStage(stage.ToString(), "failed", sw.Elapsed.TotalSeconds);
                        span?.RecordException(e, true);
                        span?.SetError(e.Message);

                        // Resolve a contended stage's queued entry to Failed in place so it isn't left dangling
                        // as a "queued" row beside the failure. Skip on server shutdown (outer-token cancel),
                        // where the queued entry is intentionally left as-is. The transient marker tells the
                        // failure handler the terminal event is already recorded, avoiding a duplicate row.
                        if (queuedEvent != null && !token.IsCancellationRequested)
                        {
                            await _Journal.ResolveEventAsync(queuedEvent, IngestionStatusEnum.Failed, e.Message, sw.Elapsed.TotalMilliseconds, queueMs, token).ConfigureAwait(false);
                            job.StageFailureRecorded = true;
                        }
                        throw;
                    }
                }
            }
            finally
            {
                gate?.Release();
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
