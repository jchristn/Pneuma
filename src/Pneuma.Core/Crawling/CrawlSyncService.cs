namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// Runs crawl operations: lists the source through the plan's crawler, compares it with the plan's baseline
    /// (<see cref="CrawlDeltaPlanner"/>), creates or re-ingests links for new, changed, and failed objects, deletes or
    /// marks the links of objects that disappeared, and finishes the operation once its ingestion jobs finish. A run
    /// that would delete more than the plan's deletion limit is held for confirmation. Thread-safe; the caller
    /// (the scheduler) guarantees one operation per plan.
    /// </summary>
    public class CrawlSyncService
    {
        #region Public-Members

        /// <summary>Days a plan stays claimed while its operation waits for ingestion jobs.</summary>
        public const int IngestingClaimDays = 30;

        #endregion

        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CrawlerFactory _Crawlers;
        private readonly LoggingModule _Logging;
        private readonly TelemetryService? _Telemetry;
        private readonly CrawlDispatcher _Dispatcher;
        private readonly string _Header = "[CrawlSyncService] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="crawlers">Registered crawlers.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="telemetry">Telemetry for operation spans, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public CrawlSyncService(DatabaseDriverBase db, CrawlerFactory crawlers, LoggingModule logging, TelemetryService? telemetry = null)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Crawlers = crawlers ?? throw new ArgumentNullException(nameof(crawlers));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Telemetry = telemetry;
            _Dispatcher = new CrawlDispatcher(db, logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run one operation for a claimed plan: enumerate, plan the delta, and dispatch it. The operation ends
        /// Ingesting when jobs were queued (finish it later with <see cref="FinalizeAsync"/>), or finished otherwise.
        /// Never throws for a crawl problem; the operation records the failure.
        /// </summary>
        /// <param name="plan">The plan with its secrets decrypted, claimed by the caller.</param>
        /// <param name="operation">The operation (created, status Running).</param>
        /// <param name="claimToken">The token the plan was claimed with; the claim is extended while the jobs run.</param>
        /// <param name="token">Cancellation token; cancelling stops the run and marks the operation Cancelled.</param>
        /// <returns>The operation as it ended this call.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public async Task<CrawlOperation> RunAsync(CrawlPlan plan, CrawlOperation operation, string claimToken, CancellationToken token)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (String.IsNullOrEmpty(claimToken)) throw new ArgumentNullException(nameof(claimToken));

            PneumaMetrics.AdjustCrawlRunning(1);
            using (RadiantSpan? span = _Telemetry?.StartSpan("crawl " + plan.Type, SpanKindEnum.Internal))
            {
                span?.SetTag("pneuma.tenant.id", plan.TenantId);
                span?.SetTag("pneuma.crawl.plan.id", plan.Id);
                span?.SetTag("pneuma.crawl.operation.id", operation.Id);
                try
                {
                    Subject? subject = await _Db.Subjects.ReadAsync(plan.TenantId, plan.SubjectId, token).ConfigureAwait(false);
                    if (subject == null) throw new InvalidOperationException("The plan's subject no longer exists.");
                    if (String.IsNullOrWhiteSpace(subject.EmbeddingModel) || String.IsNullOrWhiteSpace(subject.InferenceModel) || String.IsNullOrWhiteSpace(subject.Collection))
                        throw new InvalidOperationException("The plan's subject has no embedding model, inference model, or collection configured.");

                    ICrawler crawler = _Crawlers.Get(plan.Type);
                    List<CrawledObject> listed = new List<CrawledObject>();
                    using (RadiantSpan? enumerateSpan = _Telemetry?.StartSpan("stage:Enumerate", SpanKindEnum.Internal))
                    {
                        await foreach (CrawledObject obj in crawler.EnumerateAsync(plan, token).ConfigureAwait(false))
                        {
                            token.ThrowIfCancellationRequested();
                            listed.Add(obj);
                        }
                        enumerateSpan?.SetTag("pneuma.crawl.enumerated", listed.Count.ToString());
                    }

                    List<CrawlObject> baseline = await _Db.CrawlObjects.EnumerateByPlanAsync(plan.TenantId, plan.Id, token).ConfigureAwait(false);
                    CrawlDelta delta = CrawlDeltaPlanner.Compute(plan, listed, baseline);
                    operation.Enumerated = delta.Enumerated;
                    operation.BytesEnumerated = delta.BytesEnumerated;
                    operation.EnumeratedUtc = DateTime.UtcNow;
                    await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();

                    using (RadiantSpan? dispatchSpan = _Telemetry?.StartSpan("stage:Dispatch", SpanKindEnum.Internal))
                    {
                        await _Dispatcher.DispatchAsync(plan, subject, operation, delta, token).ConfigureAwait(false);
                    }

                    operation.DispatchedUtc = DateTime.UtcNow;
                    List<CrawlOperationObject> recorded = await _Db.CrawlOperations.EnumerateObjectsAsync(plan.TenantId, operation.Id, token).ConfigureAwait(false);
                    if (recorded.Any(o => o.JobId != null && o.Succeeded == null))
                    {
                        operation.Status = CrawlOperationStatusEnum.Ingesting;
                        await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);
                        // No server holds an Ingesting operation (any server's scheduler finishes it), so keep the plan
                        // claimed until then; finishing or stopping the operation releases it.
                        await _Db.CrawlPlans.RenewClaimAsync(plan.TenantId, plan.Id, claimToken, DateTime.UtcNow.AddDays(IngestingClaimDays), CancellationToken.None).ConfigureAwait(false);
                    }
                    else
                    {
                        await FinishAsync(plan, operation, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    operation.Status = CrawlOperationStatusEnum.Cancelled;
                    operation.Error = "Stopped before it finished.";
                    await CancelJobsAsync(operation, CancellationToken.None).ConfigureAwait(false);
                    await FinishAsync(plan, operation, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _Logging.Warn(_Header + "operation " + operation.Id + " of plan " + plan.Id + " failed: " + e.Message);
                    operation.Status = CrawlOperationStatusEnum.Failed;
                    operation.Error = e.Message;
                    await FinishAsync(plan, operation, CancellationToken.None).ConfigureAwait(false);
                }
                finally
                {
                    PneumaMetrics.AdjustCrawlRunning(-1);
                }
                span?.SetTag("pneuma.crawl.status", operation.Status.ToString());
            }
            return operation;
        }

        /// <summary>
        /// Check an Ingesting operation's jobs; record each finished job's outcome on its object, and finish the
        /// operation when none are pending.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the operation finished.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        public async Task<bool> FinalizeAsync(CrawlOperation operation, CancellationToken token)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.Status != CrawlOperationStatusEnum.Ingesting) return operation.FinishedUtc != null;

            List<CrawlOperationObject> objects = await _Db.CrawlOperations.EnumerateObjectsAsync(operation.TenantId, operation.Id, token).ConfigureAwait(false);
            List<CrawlOperationObject> changed = new List<CrawlOperationObject>();
            List<CrawlObject> baselineChanges = new List<CrawlObject>();
            Dictionary<string, CrawlObject>? baselineByKey = null;
            bool pending = false;

            foreach (CrawlOperationObject obj in objects.Where(o => o.JobId != null && o.Succeeded == null))
            {
                IngestionJob? job = await _Db.IngestionJobs.ReadAsync(operation.TenantId, obj.JobId!, token).ConfigureAwait(false);
                bool? outcome;
                string? error = null;
                if (job == null)
                {
                    outcome = false;
                    error = "The ingestion job was deleted.";
                }
                else if (job.Status == IngestionStatusEnum.Completed) outcome = true;
                else if (job.Status == IngestionStatusEnum.Failed || job.Status == IngestionStatusEnum.Cancelled)
                {
                    outcome = false;
                    error = job.Error ?? ("The ingestion job ended " + job.Status + ".");
                }
                else
                {
                    pending = true;
                    continue;
                }

                obj.Succeeded = outcome;
                if (outcome == false) obj.Detail = error;
                changed.Add(obj);

                if (baselineByKey == null)
                {
                    List<CrawlObject> baseline = await _Db.CrawlObjects.EnumerateByPlanAsync(operation.TenantId, operation.PlanId, token).ConfigureAwait(false);
                    baselineByKey = ByKey(baseline);
                }
                CrawlObject? tracked;
                if (baselineByKey.TryGetValue(obj.ExternalKey, out tracked) && tracked.LastOperationId == operation.Id)
                {
                    tracked.Status = outcome == true ? CrawlObjectStatusEnum.Active : CrawlObjectStatusEnum.Failed;
                    tracked.LastError = outcome == true ? null : error;
                    baselineChanges.Add(tracked);
                }
            }

            if (changed.Count > 0) await _Db.CrawlOperations.UpdateObjectsAsync(changed, token).ConfigureAwait(false);
            if (baselineChanges.Count > 0) await _Db.CrawlObjects.UpdateManyAsync(baselineChanges, token).ConfigureAwait(false);
            if (pending) return false;

            operation.Failed = objects.Count(o => o.Succeeded == false);
            CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(operation.TenantId, operation.PlanId, token).ConfigureAwait(false);
            await FinishAsync(plan, operation, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Run the deletions a Held operation is waiting on: mark their links for deletion and drop their objects from
        /// the baseline. The operation then finishes as Succeeded (or PartiallySucceeded when objects failed).
        /// </summary>
        /// <param name="operation">The Held operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many links were marked for deletion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the operation is not Held.</exception>
        public async Task<int> ConfirmDeletionsAsync(CrawlOperation operation, CancellationToken token)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (operation.Status != CrawlOperationStatusEnum.Held) throw new InvalidOperationException("Only a Held operation has deletions to confirm.");

            List<CrawlOperationObject> objects = await _Db.CrawlOperations.EnumerateObjectsAsync(operation.TenantId, operation.Id, token).ConfigureAwait(false);
            List<CrawlOperationObject> held = objects.Where(o => o.Action == CrawlActionEnum.Delete && o.Succeeded == null).ToList();
            List<CrawlObject> baseline = await _Db.CrawlObjects.EnumerateByPlanAsync(operation.TenantId, operation.PlanId, token).ConfigureAwait(false);
            Dictionary<string, CrawlObject> byKey = ByKey(baseline);

            int deleted = await _Dispatcher.DeleteObjectsAsync(operation.TenantId, held.Select(h => byKey.TryGetValue(h.ExternalKey, out CrawlObject? b) ? b : null).Where(b => b != null).Select(b => b!).ToList(), token).ConfigureAwait(false);
            foreach (CrawlOperationObject obj in held)
            {
                obj.Succeeded = true;
                obj.Detail = "Deletion confirmed.";
            }
            await _Db.CrawlOperations.UpdateObjectsAsync(held, token).ConfigureAwait(false);

            operation.Deleted += deleted;
            operation.HeldDeletions = 0;
            operation.Status = operation.Failed > 0 ? CrawlOperationStatusEnum.PartiallySucceeded : CrawlOperationStatusEnum.Succeeded;
            await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);
            PneumaMetrics.RecordCrawlObjects(TypeOf(await _Db.CrawlPlans.ReadAsync(operation.TenantId, operation.PlanId, token).ConfigureAwait(false)), CrawlActionEnum.Delete.ToString(), deleted);
            return deleted;
        }

        /// <summary>
        /// Stop an Ingesting operation: cancel its jobs that have not started, and finish it as Cancelled. Jobs already
        /// processing run to completion.
        /// </summary>
        /// <param name="operation">The operation.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation"/> is null.</exception>
        public async Task CancelIngestingAsync(CrawlOperation operation, CancellationToken token)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            await CancelJobsAsync(operation, token).ConfigureAwait(false);
            operation.Status = CrawlOperationStatusEnum.Cancelled;
            operation.Error = "Stopped before its ingestion jobs finished.";
            CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(operation.TenantId, operation.PlanId, token).ConfigureAwait(false);
            await FinishAsync(plan, operation, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Preview a plan: list the source and plan the delta against the baseline (empty for a draft) without writing
        /// anything.
        /// </summary>
        /// <param name="plan">The plan with its secrets decrypted.</param>
        /// <param name="isStored">True when the plan is stored (its baseline is compared); false for a draft.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="plan"/> is null.</exception>
        public async Task<CrawlPreview> PreviewAsync(CrawlPlan plan, bool isStored, CancellationToken token)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            ICrawler crawler = _Crawlers.Get(plan.Type);
            List<CrawledObject> listed = new List<CrawledObject>();
            await foreach (CrawledObject obj in crawler.EnumerateAsync(plan, token).ConfigureAwait(false)) listed.Add(obj);
            List<CrawlObject> baseline = isStored
                ? await _Db.CrawlObjects.EnumerateByPlanAsync(plan.TenantId, plan.Id, token).ConfigureAwait(false)
                : new List<CrawlObject>();
            CrawlDelta delta = CrawlDeltaPlanner.Compute(plan, listed, baseline);

            CrawlPreview preview = new CrawlPreview
            {
                Enumerated = delta.Enumerated,
                BytesEnumerated = delta.BytesEnumerated,
                Add = delta.Count(CrawlActionEnum.Add),
                Update = delta.Count(CrawlActionEnum.Update),
                Retry = delta.Count(CrawlActionEnum.Retry),
                Unchanged = delta.Count(CrawlActionEnum.Unchanged),
                Delete = delta.Count(CrawlActionEnum.Delete),
                Missing = delta.Count(CrawlActionEnum.Missing),
                Skip = delta.Count(CrawlActionEnum.Skip),
                DeletionsHeld = delta.DeletionsHeld
            };
            List<CrawlDeltaItem> acted = delta.Items.Where(i => i.Action != CrawlActionEnum.Unchanged).ToList();
            preview.Truncated = acted.Count > preview.MaxItems;
            preview.Items = acted.Take(preview.MaxItems).Select(i => new CrawlPreviewItem
            {
                Key = i.Key,
                Action = i.Action,
                ContentType = i.Source?.ContentType ?? i.Baseline?.ContentType,
                SizeBytes = i.Source?.SizeBytes ?? i.Baseline?.SizeBytes ?? 0,
                Detail = i.Detail
            }).ToList();
            return preview;
        }

        #endregion

        #region Private-Methods

        private async Task CancelJobsAsync(CrawlOperation operation, CancellationToken token)
        {
            List<CrawlOperationObject> objects = await _Db.CrawlOperations.EnumerateObjectsAsync(operation.TenantId, operation.Id, token).ConfigureAwait(false);
            List<CrawlOperationObject> changed = new List<CrawlOperationObject>();
            foreach (CrawlOperationObject obj in objects.Where(o => o.JobId != null && o.Succeeded == null))
            {
                IngestionJob? job = await _Db.IngestionJobs.ReadAsync(operation.TenantId, obj.JobId!, token).ConfigureAwait(false);
                if (job != null && job.Status == IngestionStatusEnum.Queued)
                {
                    job.Status = IngestionStatusEnum.Cancelled;
                    job.Error = "Cancelled: the crawl operation was stopped.";
                    job.CompletedUtc = DateTime.UtcNow;
                    await _Db.IngestionJobs.UpdateAsync(job, token).ConfigureAwait(false);
                    obj.Succeeded = false;
                    obj.Detail = job.Error;
                    changed.Add(obj);
                }
            }
            if (changed.Count > 0) await _Db.CrawlOperations.UpdateObjectsAsync(changed, token).ConfigureAwait(false);
        }

        private async Task FinishAsync(CrawlPlan? plan, CrawlOperation operation, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            if (operation.Status == CrawlOperationStatusEnum.Running || operation.Status == CrawlOperationStatusEnum.Ingesting)
            {
                if (operation.HeldDeletions > 0) operation.Status = CrawlOperationStatusEnum.Held;
                else if (operation.Failed == 0) operation.Status = CrawlOperationStatusEnum.Succeeded;
                else if (operation.Added + operation.Updated + operation.Retried + operation.Unchanged + operation.Deleted > operation.Failed) operation.Status = CrawlOperationStatusEnum.PartiallySucceeded;
                else operation.Status = CrawlOperationStatusEnum.Failed;
            }
            operation.FinishedUtc = now;
            await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);
            PneumaMetrics.RecordCrawlOperation(TypeOf(plan), operation.Status.ToString(), (now - operation.StartedUtc).TotalSeconds);

            if (plan == null) return;
            plan.Status = CrawlPlanStatusEnum.Idle;
            plan.LastOperationId = operation.Id;
            if (operation.Status == CrawlOperationStatusEnum.Succeeded) plan.LastSuccessUtc = now;
            await _Db.CrawlPlans.UpdateRunStateAsync(plan, token).ConfigureAwait(false);
            _Logging.Info(_Header + "operation " + operation.Id + " of plan " + plan.Id + " finished " + operation.Status +
                " (added " + operation.Added + ", updated " + operation.Updated + ", retried " + operation.Retried + ", unchanged " + operation.Unchanged +
                ", deleted " + operation.Deleted + ", missing " + operation.Missing + ", skipped " + operation.Skipped + ", failed " + operation.Failed + ")");
        }

        private static Dictionary<string, CrawlObject> ByKey(List<CrawlObject> objects)
        {
            Dictionary<string, CrawlObject> result = new Dictionary<string, CrawlObject>(StringComparer.Ordinal);
            foreach (CrawlObject obj in objects)
            {
                if (!result.ContainsKey(obj.ExternalKey)) result[obj.ExternalKey] = obj;
            }
            return result;
        }

        private static string TypeOf(CrawlPlan? plan)
        {
            return plan == null ? "(unknown)" : plan.Type.ToString();
        }

        #endregion
    }
}
