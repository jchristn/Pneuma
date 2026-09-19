namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Observability;
    using Radiant;

    /// <summary>
    /// Runs a single <see cref="IStage"/> with the uniform cross-cutting behavior every pipeline step shares:
    /// an operator-stop check, the runtime concurrency gate (with a "waiting for a slot" event when contended),
    /// the per-stage timeout, a telemetry span, the per-stage metric, and the terminal log event (resolved in
    /// place when the stage was queued). Centralizing this here means no step can accidentally bypass the gate,
    /// timeout, or telemetry — every unit of ingestion work goes through exactly one path.
    /// </summary>
    public class StageRunner
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IngestionJournal _Journal;
        private readonly ConcurrencyManager _Concurrency;
        private readonly TelemetryService _Telemetry;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the runner.</summary>
        /// <param name="db">Database driver (operator-stop re-check).</param>
        /// <param name="journal">Ingestion journal (events and status).</param>
        /// <param name="concurrency">Concurrency manager (stage gate + effective timeout).</param>
        /// <param name="telemetry">Telemetry service (per-stage spans).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public StageRunner(DatabaseDriverBase db, IngestionJournal journal, ConcurrencyManager concurrency, TelemetryService telemetry)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _Concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
            _Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one stage against the shared context. The stage reads its inputs from and writes its outputs to
        /// <paramref name="context"/> and sets <see cref="StageContext.Message"/> to the completion line.
        /// </summary>
        /// <param name="stage">The stage to run.</param>
        /// <param name="context">The per-job pipeline context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="JobCancelledException">Thrown when the operator cancelled the job out-of-band.</exception>
        public async Task RunAsync(IStage stage, StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;

            // Honor an operator "Stop": if the job was cancelled out-of-band, abort before the next stage.
            IngestionJob? current = await _Db.IngestionJobs.ReadAsync(job.TenantId, job.Id, token).ConfigureAwait(false);
            if (current != null && current.Status == IngestionStatusEnum.Cancelled) throw new JobCancelledException();

            job.Stage = stage.Stage;
            await _Journal.UpdateJobAsync(job, token).ConfigureAwait(false);

            // Per-stage concurrency gate (runtime-adjustable via the ConcurrencyManager / Padlock): bound how
            // many jobs run this stage at once, per subject or by the shared system default. Acquisition happens
            // outside the stage timeout so time spent waiting for a slot is not charged against it. When no slot
            // is immediately free, surface a "waiting" event so the log makes the contention visible.
            IngestionJobEvent? queuedEvent = null;
            double queueMs = 0;
            Task<IDisposable> acquire = _Concurrency.AcquireStageAsync(stage.Stage, job.SubjectId, token);
            Stopwatch queueSw = Stopwatch.StartNew();
            if (!acquire.IsCompleted)
            {
                // Surface a "waiting for a slot" event so contention is visible in the log. This is best-effort
                // telemetry: a failure here must NOT abandon the pending acquire below — otherwise the permit it
                // is about to be granted would be held forever with nothing to dispose it (a leak that, at a
                // per-stage cap of 1, permanently wedges the stage). So we swallow and still await the acquire.
                try
                {
                    queuedEvent = await _Journal.RecordEventAsync(job, stage.Stage, IngestionStatusEnum.Queued,
                        "Waiting for a free slot at this step — other documents are being processed. It will start automatically once one frees up.",
                        0, token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Non-fatal: the queued event is cosmetic. Cancellation still surfaces from the acquire await.
                }
            }

            // Always take ownership of the acquired permit, then immediately enter the try so the finally below is
            // guaranteed to release it. Nothing between here and the try may throw. If the acquire is cancelled it
            // throws before returning a permit (SemaphoreSlim consumes none on a cancelled wait), so there is
            // nothing to release.
            IDisposable slot = await acquire.ConfigureAwait(false);
            try
            {
                queueSw.Stop();
                if (queuedEvent != null)
                {
                    queueMs = queueSw.Elapsed.TotalMilliseconds;
                    // The slot is held and the stage is about to run: flip the "waiting for a slot" entry to
                    // Processing (carrying the wait time) so the live view and the follow-logs show this step as
                    // RUNNING, not still waiting — otherwise a job actively executing a contended stage looks
                    // stuck in the queue for the stage's whole (possibly long) duration. Best-effort.
                    try
                    {
                        await _Journal.ResolveEventAsync(queuedEvent, IngestionStatusEnum.Processing, "Running this step.", 0, queueMs, token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Non-fatal: failing to flip the event does not affect the stage running.
                    }
                }
                using (RadiantSpan? span = _Telemetry.StartSpan("stage:" + stage.Stage, SpanKindEnum.Internal))
                {
                    span?.SetTag("pneuma.stage", stage.Stage.ToString());
                    span?.SetTag("pneuma.job.id", job.Id);

                    Stopwatch sw = Stopwatch.StartNew();
                    try
                    {
                        using (CancellationTokenSource stageCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            stageCts.CancelAfter(TimeSpan.FromSeconds(_Concurrency.EffectiveStageTimeoutSeconds(job.SubjectId)));
                            context.Message = String.Empty;
                            await stage.ExecuteAsync(context, stageCts.Token).ConfigureAwait(false);
                            sw.Stop();
                            PneumaMetrics.RecordIngestionStage(stage.Stage.ToString(), "ok", sw.Elapsed.TotalSeconds);
                            span?.SetOk(null);

                            string message = String.IsNullOrEmpty(context.Message) ? (stage.Stage + " complete.") : context.Message;
                            // If the stage was contended, update its queued entry in place (carrying the wait
                            // time) rather than appending a second row for the same stage.
                            if (queuedEvent != null)
                            {
                                await _Journal.ResolveEventAsync(queuedEvent, IngestionStatusEnum.Completed, message, sw.Elapsed.TotalMilliseconds, queueMs, token).ConfigureAwait(false);
                            }
                            else
                            {
                                await _Journal.RecordEventAsync(job, stage.Stage, IngestionStatusEnum.Completed, message, sw.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        sw.Stop();
                        PneumaMetrics.RecordIngestionStage(stage.Stage.ToString(), "failed", sw.Elapsed.TotalSeconds);
                        span?.RecordException(e, true);
                        span?.SetError(e.Message);

                        // Resolve a contended stage's queued entry to Failed in place so it isn't left dangling
                        // beside the failure. Skip on server shutdown (outer-token cancel), where the queued entry
                        // is intentionally left as-is. The transient marker tells the failure handler the terminal
                        // event is already recorded, avoiding a duplicate row.
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
                slot.Dispose();
            }
        }

        #endregion
    }
}
