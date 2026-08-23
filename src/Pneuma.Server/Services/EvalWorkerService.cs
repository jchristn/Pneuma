namespace Pneuma.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using SyslogLogging;

    /// <summary>
    /// Background worker that claims queued (<c>Pending</c>) evaluation runs and processes them one at a time.
    /// Each fact's answer + judge is admitted through the shared model-runner gate (via <see cref="EvalService"/>),
    /// so a run yields to interactive query/chat traffic under load. Mirrors <see cref="IngestionWorkerService"/>.
    /// </summary>
    public class EvalWorkerService
    {
        #region Private-Members

        private const int _PollIntervalMs = 2000;

        private readonly DatabaseDriverBase _Db;
        private readonly EvalService _Eval;
        private readonly LoggingModule _Logging;
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the eval worker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="eval">Evaluation service (gate-aware) used to process a claimed run.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public EvalWorkerService(DatabaseDriverBase db, EvalService eval, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Eval = eval ?? throw new ArgumentNullException(nameof(eval));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the worker loop.</summary>
        /// <param name="token">Cancellation token that stops the loop.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => RunAsync(token), token);
            _Logging.Info("[EvalWorker] started");
        }

        #endregion

        #region Private-Methods

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                EvalRun? run = null;
                try
                {
                    run = await _Db.EvalRuns.ClaimNextQueuedAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[EvalWorker] claim error: " + e.Message);
                }

                if (run == null)
                {
                    try
                    {
                        await Task.Delay(_PollIntervalMs, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    continue;
                }

                try
                {
                    await _Eval.ProcessAsync(run, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[EvalWorker] run " + run.Id + " threw: " + e.Message);
                }
            }

            _Logging.Info("[EvalWorker] stopped");
        }

        #endregion
    }
}
