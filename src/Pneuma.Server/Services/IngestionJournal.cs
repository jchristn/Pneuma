namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using SyslogLogging;

    /// <summary>
    /// Writes an ingestion job's lifecycle to the database: per-stage log events (surfaced in the
    /// dashboard "Follow Logs"), job- and link-status transitions, terminal success/failure with metrics,
    /// and best-effort artifact persistence. Centralizing these writes lets the pipeline stages focus on
    /// producing output while the journal owns how progress is recorded.
    /// </summary>
    public class IngestionJournal
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the journal.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public IngestionJournal(DatabaseDriverBase db, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Record a per-stage log event for a job.</summary>
        /// <param name="job">The job.</param>
        /// <param name="stage">The stage the event belongs to.</param>
        /// <param name="status">The status at this event.</param>
        /// <param name="message">The human-readable message shown in the log.</param>
        /// <param name="durationMs">The elapsed time this event represents, in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created event (so a queued entry can later be resolved in place).</returns>
        public async Task<IngestionJobEvent> RecordEventAsync(IngestionJob job, IngestionStageEnum stage, IngestionStatusEnum status, string message, double durationMs, CancellationToken token)
        {
            IngestionJobEvent jobEvent = new IngestionJobEvent
            {
                TenantId = job.TenantId,
                JobId = job.Id,
                SubjectId = job.SubjectId,
                Stage = stage,
                Status = status,
                Message = message,
                DurationMs = durationMs
            };
            await _Db.IngestionJobEvents.CreateAsync(jobEvent, token).ConfigureAwait(false);
            return jobEvent;
        }

        /// <summary>
        /// Resolve an existing (typically queued) stage event to its terminal state in place, recording the
        /// stage runtime and the time it spent waiting for a free concurrency slot — so a contended stage shows
        /// as a single entry that updates, rather than a "queued" row followed by a duplicate terminal row.
        /// </summary>
        /// <param name="jobEvent">The event to update (as returned by <see cref="RecordEventAsync"/>).</param>
        /// <param name="status">The terminal status to set.</param>
        /// <param name="message">The terminal message.</param>
        /// <param name="durationMs">The stage runtime in milliseconds.</param>
        /// <param name="queueMs">The time spent waiting for a free slot, in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="jobEvent"/> is null.</exception>
        public async Task ResolveEventAsync(IngestionJobEvent jobEvent, IngestionStatusEnum status, string message, double durationMs, double queueMs, CancellationToken token)
        {
            if (jobEvent == null) throw new ArgumentNullException(nameof(jobEvent));
            jobEvent.Status = status;
            jobEvent.Message = message;
            jobEvent.DurationMs = durationMs;
            jobEvent.QueueDurationMs = queueMs;
            await _Db.IngestionJobEvents.UpdateAsync(jobEvent, token).ConfigureAwait(false);
        }

        /// <summary>Persist a job's current state, stamping its last-update time.</summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task UpdateJobAsync(IngestionJob job, CancellationToken token)
        {
            job.LastUpdateUtc = DateTime.UtcNow;
            await _Db.IngestionJobs.UpdateAsync(job, token).ConfigureAwait(false);
        }

        /// <summary>Transition the originating content link's status (and last error / last-ingested time).</summary>
        /// <param name="job">The job whose link is updated.</param>
        /// <param name="status">The new link status.</param>
        /// <param name="error">The last error, or null.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task UpdateLinkAsync(IngestionJob job, SubjectLinkStatusEnum status, string? error, CancellationToken token)
        {
            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(job.TenantId, job.LinkId, token).ConfigureAwait(false);
            if (link == null) return;
            link.Status = status;
            link.LastError = error;
            if (status == SubjectLinkStatusEnum.Ingested) link.LastIngestedUtc = DateTime.UtcNow;
            await _Db.SubjectLinks.UpdateAsync(link, token).ConfigureAwait(false);
        }

        /// <summary>Mark a job completed: update status, record the terminal event, mark the link ingested, and count metrics.</summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task CompleteAsync(IngestionJob job, CancellationToken token)
        {
            job.Status = IngestionStatusEnum.Completed;
            job.Stage = IngestionStageEnum.Done;
            job.CompletedUtc = DateTime.UtcNow;
            job.Error = null;
            await UpdateJobAsync(job, token).ConfigureAwait(false);
            await RecordEventAsync(job, IngestionStageEnum.Done, IngestionStatusEnum.Completed, "Ingestion complete.", 0, token).ConfigureAwait(false);
            await UpdateLinkAsync(job, SubjectLinkStatusEnum.Ingested, null, token).ConfigureAwait(false);
            PneumaMetrics.RecordIngestionCompleted();
            PneumaMetrics.RecordIngestionJob("completed");
        }

        /// <summary>Mark a job failed at a stage: update status, record the failure event, fail the link, and count metrics.</summary>
        /// <param name="job">The job.</param>
        /// <param name="stage">The stage the failure occurred in.</param>
        /// <param name="error">The failure message.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task FailAsync(IngestionJob job, IngestionStageEnum stage, string error, CancellationToken token)
        {
            job.Status = IngestionStatusEnum.Failed;
            job.Stage = stage;
            job.Error = error;
            job.CompletedUtc = DateTime.UtcNow;
            await UpdateJobAsync(job, token).ConfigureAwait(false);
            if (job.StageFailureRecorded)
            {
                // The failing stage's event was already updated in place (a contended stage's queued entry was
                // resolved to Failed), so don't insert a duplicate row — just clear the transient marker.
                job.StageFailureRecorded = false;
            }
            else
            {
                await RecordEventAsync(job, stage, IngestionStatusEnum.Failed, error, 0, token).ConfigureAwait(false);
            }
            await UpdateLinkAsync(job, SubjectLinkStatusEnum.Failed, error, token).ConfigureAwait(false);
            PneumaMetrics.RecordIngestionFailed();
            PneumaMetrics.RecordIngestionJob("failed");
        }

        /// <summary>Persist an artifact best-effort; a storage failure is logged but never fails the job.</summary>
        /// <param name="what">A short label for the artifact (for logging).</param>
        /// <param name="write">The write action.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task TryStoreAsync(string what, Func<Task> write, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            try
            {
                await write().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Artifact persistence is best-effort: a storage failure must never fail the ingestion job.
                _Logging.Warn("[IngestionJournal] failed to persist " + what + " artifact: " + e.Message);
            }
        }

        #endregion
    }
}
