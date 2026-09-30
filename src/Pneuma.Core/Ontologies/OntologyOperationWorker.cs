namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using SyslogLogging;

    /// <summary>
    /// Background worker for ontology operations. At start it fails operations a stopped server left running; then it
    /// claims queued operations one at a time (atomically, so two servers never run the same one) and processes them.
    /// Between polls it prunes classification cache entries unused past the retention period.
    /// </summary>
    public class OntologyOperationWorker
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly OntologyOperationProcessor _Processor;
        private readonly ClassificationCache _Cache;
        private readonly OntologySettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly string _ClaimToken = Guid.NewGuid().ToString("N");
        private DateTime _NextPruneUtc = DateTime.UtcNow.AddMinutes(5);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="processor">Operation processor.</param>
        /// <param name="cache">Classification cache (pruned between polls).</param>
        /// <param name="settings">Ontology settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public OntologyOperationWorker(DatabaseDriverBase db, OntologyOperationProcessor processor, ClassificationCache cache, OntologySettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _Cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the worker loop (no-op when <see cref="OntologySettings.WorkerEnabled"/> is false).</summary>
        /// <param name="token">Cancellation token that stops the loop.</param>
        public void Start(CancellationToken token)
        {
            if (!_Settings.WorkerEnabled)
            {
                _Logging.Info("[OntologyWorker] disabled by Ontology.WorkerEnabled");
                return;
            }
            _ = Task.Run(() => RunAsync(token), token);
            _Logging.Info("[OntologyWorker] started");
        }

        /// <summary>Claim and process one queued operation, if any (used by the loop and by tests).</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The processed operation, or null when none was queued.</returns>
        public async Task<OntologyOperation?> RunOnceAsync(CancellationToken token = default)
        {
            OntologyOperation? claimed = await _Db.OntologyOperations.ClaimNextQueuedAsync(_ClaimToken, token).ConfigureAwait(false);
            if (claimed == null) return null;
            return await _Processor.ProcessAsync(claimed, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            try
            {
                int failed = await _Db.OntologyOperations.FailRunningAsync("The server stopped while the operation was running.", token).ConfigureAwait(false);
                if (failed > 0) _Logging.Warn("[OntologyWorker] failed " + failed + " operation(s) left running by a stopped server");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn("[OntologyWorker] startup recovery failed: " + e.Message);
            }

            while (!token.IsCancellationRequested)
            {
                OntologyOperation? processed = null;
                try
                {
                    processed = await RunOnceAsync(token).ConfigureAwait(false);
                    if (processed == null && DateTime.UtcNow >= _NextPruneUtc)
                    {
                        _NextPruneUtc = DateTime.UtcNow.AddHours(1);
                        int pruned = await _Cache.PruneAsync(_Settings.CacheRetentionDays, token).ConfigureAwait(false);
                        if (pruned > 0) _Logging.Info("[OntologyWorker] pruned " + pruned + " classification cache entries");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[OntologyWorker] error: " + e.Message);
                }

                if (processed != null) continue;
                try
                {
                    await Task.Delay(_Settings.WorkerPollIntervalMs, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            _Logging.Info("[OntologyWorker] stopped");
        }

        #endregion
    }
}
