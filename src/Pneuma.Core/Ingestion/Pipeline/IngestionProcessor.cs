namespace Pneuma.Core.Ingestion.Pipeline
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
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Orchestrates a single ingestion job through its two phases — categorization (content retrieval, type
    /// detection, cell extraction, ontology classification into a candidate plan) and hydration (canonicalization,
    /// graph merge, relationship consolidation, summarization, chunking, embedding, indexing). Each phase is an
    /// ordered list of <see cref="IStage"/> units run through a single <see cref="StageRunner"/> that applies the
    /// concurrency gate, per-stage timeout, telemetry, and event logging uniformly. This class owns only the
    /// cross-stage concerns: the retry loop, the phase boundaries, the delta-skip short-circuit, and terminal state.
    /// </summary>
    public class IngestionProcessor
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IngestionJournal _Journal;
        private readonly ConcurrencyManager _Concurrency;
        private readonly TelemetryService _Telemetry;
        private readonly LoggingModule _Logging;
        private readonly StageRunner _Runner;
        private readonly IReadOnlyList<IStage> _CategorizationStages;
        private readonly IReadOnlyList<IStage> _HydrationStages;
        private readonly int _MaxAttempts;
        private readonly int _RetryBackoffBaseMs;
        private readonly int _RetryBackoffMaxMs;

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
        /// <param name="settings">Ingestion settings (retry policy, embedding cache size).</param>
        /// <param name="concurrency">Runtime concurrency manager.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="telemetry">Shared telemetry service for pipeline spans.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
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
            ConcurrencyManager concurrency,
            LoggingModule logging,
            TelemetryService telemetry)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            _Concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
            _MaxAttempts = settings.MaxAttempts;
            _RetryBackoffBaseMs = settings.RetryBackoffBaseMs;
            _RetryBackoffMaxMs = settings.RetryBackoffMaxMs;

            _Journal = new IngestionJournal(db, logging);
            // One process-wide embedding cache (a global system size limit) shared by every job this worker runs.
            EmbeddingCache embeddingCache = new EmbeddingCache(settings.EmbeddingCacheSize);
            StageDependencies deps = new StageDependencies(db, processor, cipher, graphFactory, vectors, artifacts, fetcher, documentAtom, blobs, _Journal, embeddingCache, concurrency, logging);
            _Runner = new StageRunner(db, _Journal, concurrency, telemetry);

            _CategorizationStages = new List<IStage>
            {
                new ContentRetrievalStage(deps),
                new TypeDetectionStage(deps),
                new CellExtractionStage(deps),
                new ClassificationStage(deps)
            };
            _HydrationStages = new List<IStage>
            {
                new OntologyCanonicalizationStage(deps),
                new GraphMergeStage(deps),
                new RelationshipConsolidationStage(deps),
                new SummarizationStage(deps),
                new ChunkingStage(deps),
                new EmbeddingStage(deps),
                new IndexingStage(deps)
            };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Process a claimed job end-to-end, updating its status and the originating link.
        /// </summary>
        /// <param name="job">The claimed job (already marked Processing).</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="job"/> is null.</exception>
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
                // exception from a subordinate service) is retried after an exponential backoff. Deterministic hard
                // fails (unknown type, no cells, no endpoint, no collection) throw IngestionHardFailException and are
                // never retried; unchanged content completes early; an operator "Stop" and server shutdown are
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
                        StageContext context = new StageContext(job);

                        // Phase 1 — Categorization: fetch, atomize, and classify into a candidate plan. Returns false
                        // when the job was already completed early (unchanged content).
                        bool proceed = await RunCategorizationAsync(context, jobSpan, token).ConfigureAwait(false);
                        if (!proceed) return;

                        // Phase 2 — Hydration: commit the (auto-approved) candidate plan to the graph and index.
                        await RunHydrationAsync(context, token).ConfigureAwait(false);
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
                    catch (IngestionHardFailException e)
                    {
                        // Deterministic failure — re-running would fail identically, so fail now without retrying.
                        _Logging.Warn("[IngestionProcessor] job " + job.Id + " failed (non-retryable) at " + e.Stage + ": " + e.Message);
                        jobSpan?.SetError(e.Message);
                        await _Journal.FailAsync(job, e.Stage, e.Message, token).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        // A per-stage timeout is treated as transient and retried.
                        string message = "Stage '" + job.Stage + "' timed out after " + _Concurrency.EffectiveStageTimeoutSeconds(job.SubjectId) + " seconds.";
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

        /// <summary>
        /// Run the categorization phase's ordered stages. Returns true to proceed to hydration, or false when the
        /// job was completed early because the fetched content is unchanged since the last successful ingestion.
        /// </summary>
        private async Task<bool> RunCategorizationAsync(StageContext context, RadiantSpan? jobSpan, CancellationToken token)
        {
            IngestionJob job = context.Job;
            using (RadiantSpan? phaseSpan = _Telemetry.StartSpan("phase:Categorization", SpanKindEnum.Internal))
            {
                phaseSpan?.SetTag("pneuma.phase", "Categorization");
                phaseSpan?.SetTag("pneuma.job.id", job.Id);

                Stopwatch phaseSw = Stopwatch.StartNew();
                string phaseOutcome = "ok";
                try
                {
                    foreach (IStage stage in _CategorizationStages)
                    {
                        await _Runner.RunAsync(stage, context, token).ConfigureAwait(false);

                        // Delta detection short-circuit: content retrieval found the fetched bytes unchanged since the
                        // last successful ingestion, so skip the remaining (deterministic) work and complete now.
                        if (context.CompleteEarly)
                        {
                            await _Journal.RecordEventAsync(job, IngestionStageEnum.Categorization, IngestionStatusEnum.Completed,
                                "Source content is unchanged since the last successful ingestion (matching content hash) — skipping re-processing.",
                                0, token).ConfigureAwait(false);
                            await _Journal.CompleteAsync(job, token, context.ContentHash).ConfigureAwait(false);
                            jobSpan?.SetOk(null);
                            return false;
                        }
                    }

                    // End of categorization: the candidate plan is persisted and, under auto-approval, flows straight
                    // into hydration. Emitted as Completed (not Processing) so the phase reads as finished; the prompt
                    // provenance is folded in rather than logged as a separate row.
                    await _Journal.RecordEventAsync(job, IngestionStageEnum.Categorization, IngestionStatusEnum.Completed,
                        "Categorization complete — candidate plan proposes " + context.Subgraph.Nodes.Count + " node(s) and " + context.Subgraph.Edges.Count +
                        " relationship(s); auto-approved, proceeding to hydration. Prompt provenance (for reproducibility): " + context.Provenance + ".",
                        phaseSw.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);
                    return true;
                }
                catch (Exception)
                {
                    phaseOutcome = "failed";
                    throw;
                }
                finally
                {
                    phaseSw.Stop();
                    PneumaMetrics.RecordIngestionStage("Categorization", phaseOutcome, phaseSw.Elapsed.TotalSeconds);
                    if (phaseOutcome == "ok") phaseSpan?.SetOk(null);
                }
            }
        }

        /// <summary>Run the hydration phase's ordered stages and complete the job.</summary>
        private async Task RunHydrationAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            using (RadiantSpan? phaseSpan = _Telemetry.StartSpan("phase:Hydration", SpanKindEnum.Internal))
            {
                phaseSpan?.SetTag("pneuma.phase", "Hydration");
                phaseSpan?.SetTag("pneuma.job.id", job.Id);

                Stopwatch phaseSw = Stopwatch.StartNew();
                string phaseOutcome = "ok";
                try
                {
                    await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Processing,
                        "Hydration started — committing the candidate plan to the knowledge graph and search index.",
                        0, token).ConfigureAwait(false);

                    foreach (IStage stage in _HydrationStages)
                    {
                        await _Runner.RunAsync(stage, context, token).ConfigureAwait(false);
                    }

                    // Close out the hydration phase so it does not linger as "Processing" after its sub-stages finish.
                    await _Journal.RecordEventAsync(job, IngestionStageEnum.Hydration, IngestionStatusEnum.Completed,
                        "Hydration complete — knowledge graph and search index updated.",
                        phaseSw.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);

                    await _Journal.CompleteAsync(job, token, context.ContentHash).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    phaseOutcome = "failed";
                    throw;
                }
                finally
                {
                    phaseSw.Stop();
                    PneumaMetrics.RecordIngestionStage("Hydration", phaseOutcome, phaseSw.Elapsed.TotalSeconds);
                    if (phaseOutcome == "ok") phaseSpan?.SetOk(null);
                }
            }
        }

        /// <summary>
        /// Decide whether a transiently-failed attempt should be retried and, if so, back off before the next one.
        /// Returns true when the caller should retry (having waited the backoff), false when attempts are exhausted.
        /// Bumps the persisted attempt count and records a "retrying" event so the contention is visible in the log.
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

        #endregion
    }
}
