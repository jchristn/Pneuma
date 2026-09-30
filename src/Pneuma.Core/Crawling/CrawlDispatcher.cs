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
    using SyslogLogging;

    /// <summary>
    /// Carries out a crawl operation's planned delta for <see cref="CrawlSyncService"/>: links and ingestion jobs for
    /// new, changed, and failed objects, tracked-object bookkeeping, deletions, and the operation's per-object records.
    /// </summary>
    public class CrawlDispatcher
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[CrawlDispatcher] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlDispatcher(DatabaseDriverBase db, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Act on a planned delta: create or re-ingest links for new, changed, and failed objects; mark unchanged,
        /// skipped, and missing ones; delete (or hold) removed ones; then save the tracked objects, the per-object
        /// records, and the operation's counts, and record the metrics.
        /// </summary>
        /// <param name="plan">The plan.</param>
        /// <param name="subject">The plan's subject (its models and collection go on each job).</param>
        /// <param name="operation">The running operation; its counts are updated.</param>
        /// <param name="delta">The planned delta.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task DispatchAsync(CrawlPlan plan, Subject subject, CrawlOperation operation, CrawlDelta delta, CancellationToken token)
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

        /// <summary>Mark the links of removed objects for background deletion and stop tracking the objects.</summary>
        /// <param name="tenantId">Tenant.</param>
        /// <param name="objects">The tracked objects to delete.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>How many links were marked for deletion.</returns>
        public async Task<int> DeleteObjectsAsync(string tenantId, List<CrawlObject> objects, CancellationToken token)
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

        #endregion

        #region Private-Methods

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
