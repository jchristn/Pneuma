namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Background worker that performs ingestion-job cascade deletions asynchronously. A job the operator asks to
    /// delete is marked <see cref="JobDeletionStatusEnum.Pending"/> (and Cancelled so in-flight processing stops);
    /// this worker claims it, marks it <see cref="JobDeletionStatusEnum.Deleting"/>, runs the full cascade (graph
    /// nodes/edges, index documents, raw blob, job events, and the job row), and — on failure — marks it
    /// <see cref="JobDeletionStatusEnum.Failed"/>. A job still marked Deleting at startup (an interrupted deletion)
    /// is resumed. Mirrors <see cref="LinkDeletionWorker"/>.
    /// </summary>
    public class JobDeletionWorker
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CascadeDeletionService _Cascade;
        private readonly LoggingModule _Logging;
        private readonly int _PollIntervalMs;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the job deletion worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="cascade">Cascade deletion service.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="pollIntervalMs">How often to scan for pending deletions, in milliseconds (min 500).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public JobDeletionWorker(DatabaseDriverBase db, CascadeDeletionService cascade, LoggingModule logging, int pollIntervalMs = 5000)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Cascade = cascade ?? throw new ArgumentNullException(nameof(cascade));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _PollIntervalMs = pollIntervalMs < 500 ? 500 : pollIntervalMs;
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the background deletion loop.</summary>
        /// <param name="token">Shutdown token.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => RunAsync(token), token);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    List<IngestionJob> pending = await _Db.IngestionJobs.EnumeratePendingDeletionAsync(token).ConfigureAwait(false);
                    foreach (IngestionJob job in pending)
                    {
                        if (token.IsCancellationRequested) break;
                        await DeleteOneAsync(job, token).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[JobDeletionWorker] poll error: " + e.Message);
                }

                try
                {
                    await Task.Delay(_PollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task DeleteOneAsync(IngestionJob job, CancellationToken token)
        {
            try
            {
                if (job.DeletionStatus != JobDeletionStatusEnum.Deleting)
                {
                    job.DeletionStatus = JobDeletionStatusEnum.Deleting;
                    await _Db.IngestionJobs.UpdateAsync(job, token).ConfigureAwait(false);
                }

                _Logging.Info("[JobDeletionWorker] deleting job " + job.Id + ".");
                await _Cascade.DeleteJobCascadeAsync(job.TenantId, job, token).ConfigureAwait(false);
                _Logging.Info("[JobDeletionWorker] deleted job " + job.Id + ".");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Leave it Deleting so it resumes on the next startup.
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[JobDeletionWorker] failed to delete job " + job.Id + ": " + e.Message);
                try
                {
                    IngestionJob? current = await _Db.IngestionJobs.ReadAsync(job.TenantId, job.Id, token).ConfigureAwait(false);
                    if (current != null)
                    {
                        current.DeletionStatus = JobDeletionStatusEnum.Failed;
                        await _Db.IngestionJobs.UpdateAsync(current, token).ConfigureAwait(false);
                    }
                }
                catch (Exception inner)
                {
                    _Logging.Warn("[JobDeletionWorker] could not mark job " + job.Id + " Failed: " + inner.Message);
                }
            }
        }

        #endregion
    }
}
