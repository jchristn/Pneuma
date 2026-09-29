namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using SyslogLogging;

    /// <summary>
    /// Starts, stops, and schedules crawl operations. Each pass renews this server's claims, honors stop requests,
    /// recovers operations whose server stopped, finishes operations whose ingestion jobs are done, and starts due
    /// plans. Plans are claimed atomically in the database, so several servers never run the same plan at once.
    /// Thread-safe.
    /// </summary>
    public class CrawlSchedulerService : IDisposable
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CrawlPlanService _Plans;
        private readonly CrawlSyncService _Sync;
        private readonly CrawlingSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly SemaphoreSlim _Slots;
        private readonly ConcurrentDictionary<string, RunningOperation> _Running = new ConcurrentDictionary<string, RunningOperation>(StringComparer.Ordinal);
        private readonly string _Header = "[CrawlScheduler] ";
        private Task? _Loop;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the scheduler.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="plans">Plan service.</param>
        /// <param name="sync">Sync service.</param>
        /// <param name="settings">Crawling settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlSchedulerService(DatabaseDriverBase db, CrawlPlanService plans, CrawlSyncService sync, CrawlingSettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _Sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Slots = new SemaphoreSlim(_Settings.MaxConcurrentOperations, _Settings.MaxConcurrentOperations);
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the scheduler loop (recovery first, then a pass every <see cref="CrawlingSettings.SchedulerIntervalSeconds"/>).</summary>
        /// <param name="token">Shutdown token.</param>
        public void Start(CancellationToken token)
        {
            _Loop = Task.Run(() => LoopAsync(token), token);
        }

        /// <summary>
        /// Start an operation for a plan now. Returns 202 with the operation, 404 for an unknown plan, or 409 when the
        /// plan is already running. The operation runs in the background.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="trigger">What started it.</param>
        /// <param name="token">Cancellation token for the start itself (not the run).</param>
        /// <returns>The result.</returns>
        public async Task<CrawlStartResult> StartAsync(string tenantId, string planId, CrawlTriggerEnum trigger, CancellationToken token)
        {
            CrawlPlan? plan = await _Plans.ReadWithSecretsAsync(tenantId, planId, token).ConfigureAwait(false);
            if (plan == null) return new CrawlStartResult { StatusCode = 404, Error = "Crawl plan not found." };

            string claimToken = Guid.NewGuid().ToString("N");
            DateTime now = DateTime.UtcNow;
            bool claimed = await _Db.CrawlPlans.TryClaimAsync(tenantId, planId, claimToken, now.AddMinutes(_Settings.ClaimMinutes), token).ConfigureAwait(false);
            if (!claimed) return new CrawlStartResult { StatusCode = 409, Error = "The crawl plan is already running." };

            CrawlOperation operation = new CrawlOperation
            {
                TenantId = tenantId,
                PlanId = planId,
                SubjectId = plan.SubjectId,
                Trigger = trigger,
                Status = CrawlOperationStatusEnum.Running,
                StartedUtc = now
            };
            try
            {
                await _Db.CrawlOperations.CreateAsync(operation, token).ConfigureAwait(false);
                plan.Status = CrawlPlanStatusEnum.Running;
                plan.LastOperationId = operation.Id;
                plan.LastRunUtc = now;
                plan.NextRunUtc = CrawlScheduleCalculator.NextRunUtc(plan, now);
                await _Db.CrawlPlans.UpdateRunStateAsync(plan, token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                await _Db.CrawlPlans.SetStatusAsync(tenantId, planId, CrawlPlanStatusEnum.Idle, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            RunningOperation running = new RunningOperation(tenantId, planId, operation.Id, claimToken);
            _Running[Key(tenantId, planId)] = running;
            running.Task = Task.Run(() => RunAsync(plan, operation, running));
            _Logging.Info(_Header + "started operation " + operation.Id + " for plan " + planId + " (" + trigger + ")");
            return new CrawlStartResult { StatusCode = 202, Operation = operation };
        }

        /// <summary>
        /// Stop a plan's operation: cancel it here when this server runs it, cancel its pending jobs when it is waiting
        /// on ingestion, or ask the server running it to stop. Returns 202, 404 for an unknown plan, or 409 when idle.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        public async Task<CrawlStartResult> StopAsync(string tenantId, string planId, CancellationToken token)
        {
            CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(tenantId, planId, token).ConfigureAwait(false);
            if (plan == null) return new CrawlStartResult { StatusCode = 404, Error = "Crawl plan not found." };
            if (plan.Status == CrawlPlanStatusEnum.Idle) return new CrawlStartResult { StatusCode = 409, Error = "The crawl plan is not running." };

            CrawlOperation? operation = plan.LastOperationId == null ? null : await _Db.CrawlOperations.ReadAsync(tenantId, plan.LastOperationId, token).ConfigureAwait(false);
            RunningOperation? running;
            if (_Running.TryGetValue(Key(tenantId, planId), out running))
            {
                await _Db.CrawlPlans.SetStatusAsync(tenantId, planId, CrawlPlanStatusEnum.Stopping, token).ConfigureAwait(false);
                running.Cancel();
            }
            else if (operation != null && operation.Status == CrawlOperationStatusEnum.Ingesting)
            {
                await _Sync.CancelIngestingAsync(operation, token).ConfigureAwait(false);
            }
            else
            {
                await _Db.CrawlPlans.SetStatusAsync(tenantId, planId, CrawlPlanStatusEnum.Stopping, token).ConfigureAwait(false);
            }
            return new CrawlStartResult { StatusCode = 202, Operation = operation };
        }

        /// <summary>
        /// Run one scheduler pass now: renew claims, honor stop requests, recover stale operations, finish operations
        /// whose jobs are done, and (when scheduling is enabled) start due plans.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task RunPassAsync(CancellationToken token)
        {
            await RenewAndCheckStopsAsync(token).ConfigureAwait(false);
            await RecoverAsync(token).ConfigureAwait(false);

            List<CrawlOperation> ingesting = await _Db.CrawlOperations.EnumerateByStatusAsync(CrawlOperationStatusEnum.Ingesting, token).ConfigureAwait(false);
            foreach (CrawlOperation operation in ingesting)
            {
                if (token.IsCancellationRequested) return;
                try
                {
                    await _Sync.FinalizeAsync(operation, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "could not finish operation " + operation.Id + ": " + e.Message);
                }
            }

            if (!_Settings.SchedulerEnabled) return;
            List<CrawlPlan> due = await _Db.CrawlPlans.EnumerateDueAsync(DateTime.UtcNow, token).ConfigureAwait(false);
            foreach (CrawlPlan plan in due)
            {
                if (token.IsCancellationRequested) return;
                if (_Running.Count >= _Settings.MaxConcurrentOperations) return;
                try
                {
                    await StartAsync(plan.TenantId, plan.Id, CrawlTriggerEnum.Schedule, token).ConfigureAwait(false);
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "could not start scheduled plan " + plan.Id + ": " + e.Message);
                }
            }
        }

        /// <summary>
        /// Recover operations whose server stopped: an operation still Running whose plan claim lapsed (and that this
        /// server is not running) is marked Failed and its plan released; a busy plan with no unfinished operation is
        /// released.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        public async Task RecoverAsync(CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            List<CrawlPlan> busy = await _Db.CrawlPlans.EnumerateBusyAsync(token).ConfigureAwait(false);
            foreach (CrawlPlan plan in busy)
            {
                if (_Running.ContainsKey(Key(plan.TenantId, plan.Id))) continue;
                if (plan.ClaimExpiresUtc != null && plan.ClaimExpiresUtc.Value > now) continue;

                CrawlOperation? operation = plan.LastOperationId == null ? null : await _Db.CrawlOperations.ReadAsync(plan.TenantId, plan.LastOperationId, token).ConfigureAwait(false);
                if (operation != null && operation.Status == CrawlOperationStatusEnum.Ingesting) continue;
                if (operation != null && operation.Status == CrawlOperationStatusEnum.Running)
                {
                    operation.Status = CrawlOperationStatusEnum.Failed;
                    operation.Error = "The server running this operation stopped before it finished.";
                    operation.FinishedUtc = now;
                    await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);
                    _Logging.Warn(_Header + "recovered interrupted operation " + operation.Id + " of plan " + plan.Id);
                }
                plan.Status = CrawlPlanStatusEnum.Idle;
                await _Db.CrawlPlans.UpdateRunStateAsync(plan, token).ConfigureAwait(false);
            }
        }

        /// <summary>True when this server is running an operation for the plan.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        /// <returns>True when running here.</returns>
        public bool IsRunningHere(string tenantId, string planId)
        {
            return _Running.ContainsKey(Key(tenantId, planId));
        }

        /// <summary>Wait until this server's operation for the plan (if any) stops enumerating and dispatching.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="planId">Plan identifier.</param>
        public async Task WaitAsync(string tenantId, string planId)
        {
            RunningOperation? running;
            if (_Running.TryGetValue(Key(tenantId, planId), out running) && running.Task != null)
            {
                try { await running.Task.ConfigureAwait(false); } catch (Exception) { }
            }
        }

        /// <summary>Cancel running operations and release resources.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>Dispose resources.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                foreach (RunningOperation running in _Running.Values) running.Cancel();
            }
            _Disposed = true;
        }

        private async Task LoopAsync(CancellationToken token)
        {
            try
            {
                await RecoverAsync(token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                _Logging.Warn(_Header + "startup recovery failed: " + e.Message);
            }

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.SchedulerIntervalSeconds), token).ConfigureAwait(false);
                    await RunPassAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "pass failed: " + e.Message);
                }
            }

            foreach (RunningOperation running in _Running.Values) running.Cancel();
        }

        private async Task RunAsync(CrawlPlan plan, CrawlOperation operation, RunningOperation running)
        {
            bool slot = false;
            try
            {
                await _Slots.WaitAsync(running.Token).ConfigureAwait(false);
                slot = true;
                await _Sync.RunAsync(plan, operation, running.ClaimToken, running.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancelled while waiting for a slot: the operation never started enumerating.
                operation.Status = CrawlOperationStatusEnum.Cancelled;
                operation.Error = "Stopped before it started.";
                operation.FinishedUtc = DateTime.UtcNow;
                await _Db.CrawlOperations.UpdateAsync(operation, CancellationToken.None).ConfigureAwait(false);
                await _Db.CrawlPlans.SetStatusAsync(plan.TenantId, plan.Id, CrawlPlanStatusEnum.Idle, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "operation " + operation.Id + " ended unexpectedly: " + e.Message);
            }
            finally
            {
                if (slot) _Slots.Release();
                RunningOperation? removed;
                _Running.TryRemove(Key(plan.TenantId, plan.Id), out removed);
                running.Dispose();
            }

            try
            {
                int pruned = await _Db.CrawlOperations.DeleteFinishedBeforeAsync(plan.TenantId, plan.Id, DateTime.UtcNow.AddDays(-plan.OperationRetentionDays), CancellationToken.None).ConfigureAwait(false);
                if (pruned > 0) _Logging.Debug(_Header + "pruned " + pruned + " operations of plan " + plan.Id);
            }
            catch (Exception e)
            {
                _Logging.Warn(_Header + "could not prune operations of plan " + plan.Id + ": " + e.Message);
            }
        }

        private async Task RenewAndCheckStopsAsync(CancellationToken token)
        {
            DateTime expires = DateTime.UtcNow.AddMinutes(_Settings.ClaimMinutes);
            foreach (RunningOperation running in _Running.Values.ToList())
            {
                try
                {
                    await _Db.CrawlPlans.RenewClaimAsync(running.TenantId, running.PlanId, running.ClaimToken, expires, token).ConfigureAwait(false);
                    CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(running.TenantId, running.PlanId, token).ConfigureAwait(false);
                    if (plan == null || plan.Status == CrawlPlanStatusEnum.Stopping) running.Cancel();
                }
                catch (Exception e) when (!(e is OperationCanceledException))
                {
                    _Logging.Warn(_Header + "could not renew the claim on plan " + running.PlanId + ": " + e.Message);
                }
            }
        }

        private static string Key(string tenantId, string planId)
        {
            return tenantId + "/" + planId;
        }

        #endregion
    }
}
