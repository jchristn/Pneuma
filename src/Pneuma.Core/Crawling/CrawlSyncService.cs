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
                        await DispatchAsync(plan, subject, operation, delta, token).ConfigureAwait(false);
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

            int deleted = await DeleteObjectsAsync(operation.TenantId, held.Select(h => byKey.TryGetValue(h.ExternalKey, out CrawlObject? b) ? b : null).Where(b => b != null).Select(b => b!).ToList(), token).ConfigureAwait(false);
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

        private async Task DispatchAsync(CrawlPlan plan, Subject subject, CrawlOperation operation, CrawlDelta delta, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            List<CrawlOperationObject> records = new List<CrawlOperationObject>();
            List<CrawlObject> created = new List<CrawlObject>();
            List<CrawlObject> updated = new List<CrawlObject>();
            string type = plan.Type.ToString();

            foreach (CrawlDeltaItem item in delta.Items)
            {
                token.ThrowIfCancellationRequested();
                switch (item.Action)
                {
                    case CrawlActionEnum.Add:
                    case CrawlActionEnum.Update:
                    case CrawlActionEnum.Retry:
                        await DispatchOneAsync(plan, subject, operation, item, now, records, created, updated, token).ConfigureAwait(false);
                        break;

                    case CrawlActionEnum.Unchanged:
                        operation.Unchanged++;
                        CrawlObject same = item.Baseline!;
                        same.LastSeenUtc = now;
                        same.LastOperationId = operation.Id;
                        if (same.Status == CrawlObjectStatusEnum.Missing || same.Status == CrawlObjectStatusEnum.Excluded) same.Status = CrawlObjectStatusEnum.Active;
                        updated.Add(same);
                        break;

                    case CrawlActionEnum.Skip:
                        operation.Skipped++;
                        records.Add(Record(operation, item.Key, CrawlActionEnum.Skip, true, item.Baseline?.LinkId, null, item.Detail));
                        if (item.Baseline != null && item.Baseline.LinkId != null && item.Baseline.Status != CrawlObjectStatusEnum.Excluded)
                        {
                            // A filtered-out object that is still in the source keeps its link; it is marked so the
                            // operator can see it is no longer being refreshed.
                            item.Baseline.Status = CrawlObjectStatusEnum.Excluded;
                            item.Baseline.LastSeenUtc = now;
                            updated.Add(item.Baseline);
                        }
                        break;

                    case CrawlActionEnum.Missing:
                        operation.Missing++;
                        if (item.Baseline!.Status != CrawlObjectStatusEnum.Missing)
                        {
                            item.Baseline.Status = CrawlObjectStatusEnum.Missing;
                            updated.Add(item.Baseline);
                        }
                        break;

                    case CrawlActionEnum.Delete:
                        break;
                }
            }

            List<CrawlDeltaItem> deletions = delta.Items.Where(i => i.Action == CrawlActionEnum.Delete).ToList();
            if (deletions.Count > 0)
            {
                if (delta.DeletionsHeld)
                {
                    operation.HeldDeletions = deletions.Count;
                    operation.Error = deletions.Count + " deletions exceed the plan's limit of " + (plan.MaxDeletionFraction * 100).ToString("0.#") + "% of its links and wait for confirmation.";
                    foreach (CrawlDeltaItem item in deletions)
                        records.Add(Record(operation, item.Key, CrawlActionEnum.Delete, null, item.Baseline?.LinkId, null, "Held for confirmation."));
                }
                else
                {
                    int deleted = await DeleteObjectsAsync(plan.TenantId, deletions.Select(d => d.Baseline!).ToList(), token).ConfigureAwait(false);
                    operation.Deleted = deleted;
                    foreach (CrawlDeltaItem item in deletions)
                        records.Add(Record(operation, item.Key, CrawlActionEnum.Delete, true, item.Baseline?.LinkId, null, null));
                }
            }

            if (created.Count > 0) await _Db.CrawlObjects.CreateManyAsync(created, token).ConfigureAwait(false);
            if (updated.Count > 0) await _Db.CrawlObjects.UpdateManyAsync(updated.Distinct().ToList(), token).ConfigureAwait(false);
            if (records.Count > 0) await _Db.CrawlOperations.CreateObjectsAsync(records, token).ConfigureAwait(false);
            await _Db.CrawlOperations.UpdateAsync(operation, token).ConfigureAwait(false);

            PneumaMetrics.RecordCrawlBytes(type, operation.BytesEnumerated);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Add.ToString(), operation.Added);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Update.ToString(), operation.Updated);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Retry.ToString(), operation.Retried);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Unchanged.ToString(), operation.Unchanged);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Skip.ToString(), operation.Skipped);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Missing.ToString(), operation.Missing);
            PneumaMetrics.RecordCrawlObjects(type, CrawlActionEnum.Delete.ToString(), operation.Deleted);
        }

        private async Task DispatchOneAsync(
            CrawlPlan plan,
            Subject subject,
            CrawlOperation operation,
            CrawlDeltaItem item,
            DateTime now,
            List<CrawlOperationObject> records,
            List<CrawlObject> created,
            List<CrawlObject> updated,
            CancellationToken token)
        {
            CrawledObject source = item.Source!;
            try
            {
                SubjectLink? link = null;
                if (item.Baseline?.LinkId != null)
                {
                    link = await _Db.SubjectLinks.ReadAsync(plan.TenantId, item.Baseline.LinkId, token).ConfigureAwait(false);
                    if (link != null && link.DeletionStatus != LinkDeletionStatusEnum.None) link = null;
                }

                IngestionJob job;
                if (link == null)
                {
                    link = new SubjectLink
                    {
                        TenantId = plan.TenantId,
                        SubjectId = subject.Id,
                        Status = SubjectLinkStatusEnum.Submitted,
                        SourceKind = SourceKindEnum.Crawl,
                        CrawlPlanId = plan.Id,
                        ExternalKey = CrawlKeys.LinkExternalKey(plan.Id, source.Key)
                    };
                    ApplySource(link, plan, source);
                    job = NewJob(subject, link);
                    await _Db.SubjectLinks.CreateWithJobAsync(link, job, token).ConfigureAwait(false);
                }
                else
                {
                    ApplySource(link, plan, source);
                    link.Status = SubjectLinkStatusEnum.Submitted;
                    link.LastError = null;
                    await _Db.SubjectLinks.UpdateAsync(link, token).ConfigureAwait(false);
                    job = await _Db.IngestionJobs.CreateAsync(NewJob(subject, link), token).ConfigureAwait(false);
                }

                if (item.Action == CrawlActionEnum.Add) operation.Added++;
                else if (item.Action == CrawlActionEnum.Update) operation.Updated++;
                else operation.Retried++;
                records.Add(Record(operation, source.Key, item.Action, null, link.Id, job.Id, null));

                CrawlObject tracked = item.Baseline ?? new CrawlObject
                {
                    TenantId = plan.TenantId,
                    PlanId = plan.Id,
                    ExternalKey = source.Key,
                    FirstSeenUtc = now
                };
                tracked.LinkId = link.Id;
                tracked.VersionToken = source.VersionToken;
                tracked.SizeBytes = source.SizeBytes;
                tracked.ContentType = source.ContentType;
                tracked.Status = CrawlObjectStatusEnum.Active;
                tracked.LastError = null;
                tracked.LastOperationId = operation.Id;
                tracked.LastSeenUtc = now;
                if (item.Baseline == null) created.Add(tracked);
                else updated.Add(tracked);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                operation.Failed++;
                records.Add(Record(operation, source.Key, CrawlActionEnum.Fail, false, item.Baseline?.LinkId, null, e.Message));
                if (item.Baseline != null)
                {
                    item.Baseline.Status = CrawlObjectStatusEnum.Failed;
                    item.Baseline.LastError = e.Message;
                    item.Baseline.LastSeenUtc = now;
                    updated.Add(item.Baseline);
                }
                _Logging.Warn(_Header + "could not dispatch " + source.Key + " for plan " + plan.Id + ": " + e.Message);
            }
        }

        private static void ApplySource(SubjectLink link, CrawlPlan plan, CrawledObject source)
        {
            link.Url = String.IsNullOrEmpty(source.Uri) ? source.Key : source.Uri!;
            link.Title = String.IsNullOrWhiteSpace(source.Title) ? CrawlKeys.TitleFor(source.Key) : source.Title;
            link.ContentType = source.ContentType;
            link.SizeBytes = source.SizeBytes;
            link.Labels = new List<string>(plan.Labels);
            link.Tags = new Dictionary<string, string>(plan.Tags, StringComparer.Ordinal);
        }

        private async Task<int> DeleteObjectsAsync(string tenantId, List<CrawlObject> objects, CancellationToken token)
        {
            int marked = 0;
            foreach (CrawlObject obj in objects)
            {
                if (obj.LinkId == null) continue;
                SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, obj.LinkId, token).ConfigureAwait(false);
                if (link == null || link.DeletionStatus != LinkDeletionStatusEnum.None) continue;
                link.DeletionStatus = LinkDeletionStatusEnum.Pending;
                await _Db.SubjectLinks.UpdateAsync(link, token).ConfigureAwait(false);
                marked++;
            }
            await _Db.CrawlObjects.DeleteManyAsync(tenantId, objects.Select(o => o.Id), token).ConfigureAwait(false);
            return marked;
        }

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

        private static CrawlOperationObject Record(CrawlOperation operation, string key, CrawlActionEnum action, bool? succeeded, string? linkId, string? jobId, string? detail)
        {
            return new CrawlOperationObject
            {
                TenantId = operation.TenantId,
                OperationId = operation.Id,
                ExternalKey = key,
                Action = action,
                Succeeded = succeeded,
                LinkId = linkId,
                JobId = jobId,
                Detail = detail
            };
        }

        private static IngestionJob NewJob(Subject subject, SubjectLink link)
        {
            return new IngestionJob
            {
                TenantId = link.TenantId,
                SubjectId = subject.Id,
                LinkId = link.Id,
                SourceUrl = link.Url,
                Labels = new List<string>(link.Labels),
                Tags = new Dictionary<string, string>(link.Tags),
                Trigger = IngestionTriggerEnum.Crawl,
                Status = IngestionStatusEnum.Queued,
                Stage = IngestionStageEnum.Pending,
                EmbeddingEndpointId = subject.EmbeddingModel,
                CompletionEndpointId = subject.InferenceModel,
                CollectionId = subject.Collection
            };
        }

        #endregion
    }
}
