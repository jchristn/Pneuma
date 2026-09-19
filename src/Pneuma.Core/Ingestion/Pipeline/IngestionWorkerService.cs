namespace Pneuma.Core.Ingestion.Pipeline
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Configuration;
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
                }, token);
            }

            _Logging.Info("[IngestionWorker] stopped");
        }

        #endregion
    }
}
