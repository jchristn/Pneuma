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
    using Pneuma.Core.Integrations.Implementations;
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
        #region Public-Members

        /// <summary>
        /// Retrieves each job's content by its link's source kind. Set its <c>CrawlSource</c> so links crawl plans
        /// create can be ingested.
        /// </summary>
        public ContentResolver Resolver { get; }

        #endregion

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
        private readonly PartialLossPolicyEnum _PartialLossPolicy;
        private readonly VersionRetirementService _Retirement;

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
            _PartialLossPolicy = settings.PartialLossPolicy;

            _Journal = new IngestionJournal(db, logging);
            // One process-wide embedding cache (a global system size limit) shared by every job this worker runs.
            EmbeddingCache embeddingCache = new EmbeddingCache(settings.EmbeddingCacheSize);
            StageDependencies deps = new StageDependencies(db, processor, cipher, graphFactory, vectors, artifacts, fetcher, documentAtom, blobs, _Journal, embeddingCache, concurrency, logging);
            Resolver = deps.Resolver;
            _Runner = new StageRunner(db, _Journal, concurrency, telemetry);
            _Retirement = new VersionRetirementService(db, graphFactory, vectors, logging);

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

                // Each claim gets up to MaxAttempts inline attempts. Every failure is classified (see
                // IngestionFailureClassifier): retryable categories back off and try again, deterministic ones (an
                // unsupported type, missing configuration, a blocked URL) fail at once. Unchanged content completes
                // early; an operator "Stop" and server shutdown are handled distinctly and never retried. Every attempt
                // is recorded so the job's history shows each failure, not only the last.
                int attempt = 0;
                while (true)
                {
                    attempt++;
                    DateTime attemptStartedUtc = DateTime.UtcNow;

                    // A fresh attempt records its own stage events and re-runs every stage, so clear any in-place
                    // failure marker, warnings, and counts left by the previous attempt.
                    job.StageFailureRecorded = false;
                    job.Warnings = new List<string>();
                    job.Completeness = new IngestionCompleteness();

                    // A retry re-runs every stage, so first remove what the failed attempt already wrote (its chunks
                    // and its Source and Cell nodes); otherwise each retry would add another copy.
                    if (attempt > 1)
                    {
                        RetirementResult cleared = await _Retirement.RemoveJobOutputAsync(job, token).ConfigureAwait(false);
                        if (cleared.Errors.Count > 0)
                        {
                            _Logging.Warn("[IngestionProcessor] job " + job.Id + " could not fully clear its previous attempt: " + String.Join("; ", cleared.Errors));
                        }
                    }

                    try
                    {
                        StageContext context = new StageContext(job);

                        // Phase 1: Categorization. Fetch, atomize, and classify into a candidate plan. Returns false
                        // when the job was already completed early (unchanged content).
                        bool proceed = await RunCategorizationAsync(context, jobSpan, token).ConfigureAwait(false);
                        if (proceed)
                        {
                            // Phase 2: Hydration. Commit the (auto-approved) candidate plan to the graph and index.
                            await RunHydrationAsync(context, token).ConfigureAwait(false);
                        }

                        await _Journal.RecordAttemptAsync(job, attempt, true, job.Stage, null, null, attemptStartedUtc, token).ConfigureAwait(false);
                        jobSpan?.SetOk(null);
                        return;
                    }
                    catch (JobCancelledException)
                    {
                        // Operator pressed "Stop": the job is already marked Cancelled; record it and finish cleanly.
                        _Logging.Info("[IngestionProcessor] job " + job.Id + " cancelled by operator.");
                        PneumaMetrics.RecordIngestionJob("cancelled");
                        jobSpan?.SetError("Cancelled by operator.");
                        await _Journal.RecordAttemptAsync(job, attempt, false, job.Stage, IngestionFailureCategoryEnum.Cancelled, "Cancelled by operator.", attemptStartedUtc, token).ConfigureAwait(false);
                        await _Journal.RecordEventAsync(job, job.Stage, IngestionStatusEnum.Cancelled, "Ingestion stopped by operator.", 0, token).ConfigureAwait(false);
                        await _Journal.UpdateLinkAsync(job, SubjectLinkStatusEnum.Failed, "Cancelled by operator.", token, null, IngestionFailureCategoryEnum.Cancelled, 0).ConfigureAwait(false);
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        // Server shutdown: let the worker observe cancellation without marking the job failed.
                        throw;
                    }
                    catch (Exception e)
                    {
                        // Server shutdown surfacing as a generic exception must not mark the job failed.
                        if (token.IsCancellationRequested) throw;

                        IngestionFailureClassification classification = IngestionFailureClassifier.Classify(e, job.Stage);
                        string message = e is OperationCanceledException
                            ? "Stage '" + job.Stage + "' timed out after " + _Concurrency.EffectiveStageTimeoutSeconds(job.SubjectId) + " seconds."
                            : e.Message;

                        await _Journal.RecordAttemptAsync(job, attempt, false, job.Stage, classification.Category, message, attemptStartedUtc, token).ConfigureAwait(false);

                        if (classification.Retryable && await TryScheduleRetryAsync(job, attempt, message, classification.BackoffMultiplier, token).ConfigureAwait(false)) continue;

                        if (classification.Retryable)
                        {
                            _Logging.Warn("[IngestionProcessor] job " + job.Id + " failed (" + classification.Category + ") after " + attempt + " attempt(s): " + message);
                        }
                        else
                        {
                            _Logging.Warn("[IngestionProcessor] job " + job.Id + " failed (" + classification.Category + ", not retryable) at " + job.Stage + ": " + message);
                        }

                        jobSpan?.RecordException(e, true);
                        jobSpan?.SetError(message);
                        IngestionStageEnum failedStage = e is IngestionHardFailException hard ? hard.Stage : job.Stage;
                        await _Journal.FailAsync(job, failedStage, message, token, classification.Category).ConfigureAwait(false);
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
                            await _Journal.CompleteAsync(job, token, context.ContentHash, false).ConfigureAwait(false);
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

                    // Work dropped along the way (a failed classification batch, summary, or cell node) is recorded as
                    // warnings. Under the Fail policy the attempt fails instead, so a transient cause gets a retry.
                    if (_PartialLossPolicy == PartialLossPolicyEnum.Fail && job.Warnings.Count > 0)
                    {
                        throw new PartialLossException(new List<string>(job.Warnings));
                    }

                    // The new version is fully indexed: remove earlier versions of the link so only this one is
                    // searchable. A removal failure leaves both versions and is recorded as a warning; the next
                    // successful ingest of the link removes the leftovers.
                    RetirementResult retired = await _Retirement.RetireOlderVersionsAsync(job, token).ConfigureAwait(false);
                    if (retired.JobsRetired > 0)
                    {
                        await _Journal.RecordEventAsync(job, IngestionStageEnum.Indexing, IngestionStatusEnum.Completed,
                            "Replaced the previous version: removed the output of " + retired.JobsRetired + " earlier job(s), including " + retired.NodesDeleted + " source and cell node(s).",
                            0, token).ConfigureAwait(false);
                    }
                    foreach (string error in retired.Errors) context.AddWarning("The previous version could not be fully removed (" + error + "); it stays searchable until the next successful ingest.");

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
        /// <param name="backoffMultiplier">Multiplier on the normal backoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True to retry; false to give up.</returns>
        private async Task<bool> TryScheduleRetryAsync(IngestionJob job, int attempt, string reason, int backoffMultiplier, CancellationToken token)
        {
            if (attempt >= _MaxAttempts) return false;

            // Categories that wait on an external recovery (a rate-limited model endpoint) back off longer; the
            // configured maximum still caps the wait.
            long scaled = (long)ComputeBackoffMs(attempt) * Math.Max(1, backoffMultiplier);
            int delayMs = (int)Math.Min(scaled, (long)Math.Max(_RetryBackoffMaxMs, ComputeBackoffMs(attempt)));
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
