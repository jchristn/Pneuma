namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Server.Settings;
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
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Slots;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="processor">Ingestion processor.</param>
        /// <param name="settings">Ingestion settings.</param>
        /// <param name="logging">Logging module.</param>
        public IngestionWorkerService(DatabaseDriverBase db, IngestionProcessor processor, IngestionSettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Slots = new SemaphoreSlim(_Settings.MaxConcurrentTasks, _Settings.MaxConcurrentTasks);
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
                try
                {
                    await _Slots.WaitAsync(token).ConfigureAwait(false);
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
                    _Slots.Release();
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
                        _Slots.Release();
                    }
                }, token);
            }

            _Logging.Info("[IngestionWorker] stopped");
        }

        #endregion
    }
}
