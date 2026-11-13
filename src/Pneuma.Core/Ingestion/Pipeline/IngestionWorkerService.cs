namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Background worker that claims queued ingestion jobs and processes them with bounded concurrency.
    /// </summary>
    public class IngestionWorkerService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IngestionProcessor _Processor;
        private readonly IngestionSettings _Settings;
        private readonly ConcurrencyManager _Concurrency;
        private readonly LoggingModule _Logging;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="processor">Ingestion processor.</param>
        /// <param name="settings">Ingestion settings.</param>
        /// <param name="concurrency">Runtime concurrency manager that gates job slots.</param>
        /// <param name="logging">Logging module.</param>
        public IngestionWorkerService(DatabaseDriverBase db, IngestionProcessor processor, IngestionSettings settings, ConcurrencyManager concurrency, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Concurrency = concurrency ?? throw new ArgumentNullException(nameof(concurrency));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the worker loop.</summary>
        /// <param name="token">Cancellation token that stops the loop.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => RunAsync(token), token);
            _Logging.Info("[IngestionWorker] started with " + _Settings.MaxConcurrentTasks + " concurrent slots");
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            // Jobs a previous run left Processing (the server stopped or restarted mid-job) would otherwise never be
            // claimed again; queue them again before taking new work.
            if (_Settings.RequeueInterruptedJobs)
            {
                try
                {
                    await RecoverInterruptedAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[IngestionWorker] interrupted-job recovery failed: " + e.Message);
                }
            }

            while (!token.IsCancellationRequested)
            {
                IDisposable slot;
                try
                {
                    slot = await _Concurrency.AcquireJobSlotAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                IngestionJob? job = null;
                try
                {
                    job = await _Db.IngestionJobs.ClaimNextQueuedAsync(token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn("[IngestionWorker] claim error: " + e.Message);
                }

                if (job == null)
                {
                    slot.Dispose();
                    try
                    {
                        await Task.Delay(_Settings.PollIntervalMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                IngestionJob claimed = job;
                IDisposable held = slot;
                // Do NOT pass the cancellation token to Task.Run: a token that is already cancelled makes Task.Run
                // skip the delegate entirely, so the finally that releases the job-pool slot would never run and the
                // slot would leak. The delegate always runs and always disposes the slot; cancellation is still
                // honored inside ProcessAsync (which receives the token).
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _Processor.ProcessAsync(claimed, token).ConfigureAwait(false);
                    }
                    catch (Exception e)
                    {
                        _Logging.Warn("[IngestionWorker] job " + claimed.Id + " threw: " + e.Message);
                    }
                    finally
                    {
                        held.Dispose();
                    }
                });
            }

            _Logging.Info("[IngestionWorker] stopped");
        }

        private async Task RecoverInterruptedAsync(CancellationToken token)
        {
            List<IngestionJob> stuck = await _Db.IngestionJobs.EnumerateByStatusAsync(IngestionStatusEnum.Processing, token).ConfigureAwait(false);
            if (stuck.Count == 0) return;
            IngestionJournal journal = new IngestionJournal(_Db, _Logging);
            int requeued = 0;
            int failed = 0;
            foreach (IngestionJob job in stuck)
            {
                if (job.DeletionStatus != JobDeletionStatusEnum.None) continue;
                if (await journal.RequeueInterruptedAsync(job, _Settings.MaxInterruptedRequeues, token).ConfigureAwait(false)) requeued++;
                else failed++;
            }
            _Logging.Info("[IngestionWorker] recovered " + stuck.Count + " interrupted job(s): " + requeued + " queued again, " + failed + " failed after repeated interruptions");
        }

        #endregion
    }
}
