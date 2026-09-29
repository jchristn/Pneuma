namespace Pneuma.Core.Ingestion.Refresh
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using SyslogLogging;

    /// <summary>
    /// Re-checks URL links on their refresh schedule. Each pass claims due links (so several servers never check the
    /// same link at once), sends a conditional GET with the ETag and Last-Modified from the last check, and queues a
    /// re-ingest job with trigger Refresh only when the source changed. A 304 moves the next refresh without any work;
    /// a failed check keeps the link's current version and retries with back-off. Links a crawl plan owns, pushed
    /// content, and inactive or deleting links are never refreshed. Thread-safe.
    /// </summary>
    public class LinkRefreshService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CrawlHttpClient _Http;
        private readonly LinkRefreshSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly string _Header = "[LinkRefresh] ";
        private Task? _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="http">Policy-enforcing HTTP client for the conditional checks.</param>
        /// <param name="settings">Refresh settings.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public LinkRefreshService(DatabaseDriverBase db, CrawlHttpClient http, LinkRefreshSettings settings, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>Start the refresh loop (a pass every <see cref="LinkRefreshSettings.IntervalSeconds"/>) when enabled.</summary>
        /// <param name="token">Shutdown token.</param>
        public void Start(CancellationToken token)
        {
            if (!_Settings.Enabled) return;
            _Loop = Task.Run(() => LoopAsync(token), token);
        }

        /// <summary>Check every due link (up to the batch size) once.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A result per link checked.</returns>
        public async Task<List<LinkRefreshResult>> RunPassAsync(CancellationToken token)
        {
            List<LinkRefreshResult> results = new List<LinkRefreshResult>();
            DateTime now = DateTime.UtcNow;
            List<SubjectLink> due = await _Db.SubjectLinks.EnumerateDueForRefreshAsync(now, _Settings.BatchSize, token).ConfigureAwait(false);
            foreach (SubjectLink link in due)
            {
                if (token.IsCancellationRequested) break;
                // Claim for ten minutes; the check below always sets the real next refresh.
                DateTime claimUntil = now.AddMinutes(10).AddMilliseconds(Random.Shared.Next(1, 1000));
                if (!await _Db.SubjectLinks.TryClaimRefreshAsync(link.TenantId, link.Id, link.NextRefreshUtc, claimUntil, token).ConfigureAwait(false)) continue;
                link.NextRefreshUtc = claimUntil;
                results.Add(await CheckAsync(link, token).ConfigureAwait(false));
            }
            return results;
        }

        /// <summary>Check one link now, whatever its schedule (the "refresh now" action).</summary>
        /// <param name="link">The link.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="link"/> is null.</exception>
        public Task<LinkRefreshResult> RefreshNowAsync(SubjectLink link, CancellationToken token)
        {
            if (link == null) throw new ArgumentNullException(nameof(link));
            return CheckAsync(link, token);
        }

        /// <summary>
        /// Recompute a link's next refresh after its interval (or its subject's default) changed: now plus the effective
        /// interval with jitter, or null when refresh is off. Stored with <c>UpdateRefreshStateAsync</c> by the caller.
        /// </summary>
        /// <param name="link">The link.</param>
        /// <param name="subject">The link's subject.</param>
        public static void Reschedule(SubjectLink link, Subject? subject)
        {
            if (link == null) return;
            link.NextRefreshUtc = LinkRefreshSchedule.Next(DateTime.UtcNow, LinkRefreshSchedule.EffectiveInterval(link, subject));
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_Settings.IntervalSeconds), token).ConfigureAwait(false);
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
        }

        private async Task<LinkRefreshResult> CheckAsync(SubjectLink link, CancellationToken token)
        {
            DateTime now = DateTime.UtcNow;
            LinkRefreshResult result = new LinkRefreshResult { LinkId = link.Id };
            Subject? subject = await _Db.Subjects.ReadAsync(link.TenantId, link.SubjectId, token).ConfigureAwait(false);
            int interval = LinkRefreshSchedule.EffectiveInterval(link, subject);

            if (subject == null || interval == 0)
            {
                result.Outcome = LinkRefreshOutcomeEnum.Skipped;
                result.Message = subject == null ? "The link's subject no longer exists." : "Refresh is off for this link.";
                link.NextRefreshUtc = null;
                await SaveAsync(link, result, token).ConfigureAwait(false);
                return result;
            }

            if (link.Status == SubjectLinkStatusEnum.Submitted || link.Status == SubjectLinkStatusEnum.Processing)
            {
                // A job is already queued or running; look again after a short wait rather than queueing a second.
                result.Outcome = LinkRefreshOutcomeEnum.Busy;
                result.Message = "A job for this link is already queued or running.";
                link.NextRefreshUtc = now.AddMinutes(Math.Min(15, interval));
                await SaveAsync(link, result, token).ConfigureAwait(false);
                return result;
            }

            try
            {
                CrawlHttpResponse response = await _Http.GetConditionalAsync(link.Url, link.SourceETag, link.SourceLastModifiedUtc, token).ConfigureAwait(false);
                link.LastRefreshUtc = now;
                link.RefreshFailures = 0;
                link.SourceETag = response.ETag;
                link.SourceLastModifiedUtc = response.LastModifiedUtc;
                link.NextRefreshUtc = LinkRefreshSchedule.Next(now, interval);
                if (response.StatusCode == 304)
                {
                    result.Outcome = LinkRefreshOutcomeEnum.Unchanged;
                    await SaveAsync(link, result, token).ConfigureAwait(false);
                    return result;
                }

                // Changed (or the source gives no validators): re-ingest. The stored content hash is kept, so content that
                // turns out to be identical completes early without re-processing.
                IngestionJob job = await _Db.IngestionJobs.CreateAsync(new IngestionJob
                {
                    TenantId = link.TenantId,
                    SubjectId = subject.Id,
                    LinkId = link.Id,
                    SourceUrl = link.Url,
                    Labels = new List<string>(link.Labels),
                    Tags = new Dictionary<string, string>(link.Tags),
                    Trigger = IngestionTriggerEnum.Refresh,
                    Status = IngestionStatusEnum.Queued,
                    Stage = IngestionStageEnum.Pending,
                    EmbeddingEndpointId = subject.EmbeddingModel,
                    CompletionEndpointId = subject.InferenceModel,
                    CollectionId = subject.Collection
                }, token).ConfigureAwait(false);
                SubjectLink? current = await _Db.SubjectLinks.ReadAsync(link.TenantId, link.Id, token).ConfigureAwait(false);
                if (current != null)
                {
                    current.Status = SubjectLinkStatusEnum.Submitted;
                    await _Db.SubjectLinks.UpdateAsync(current, token).ConfigureAwait(false);
                }
                result.Outcome = LinkRefreshOutcomeEnum.Queued;
                result.JobId = job.Id;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                link.LastRefreshUtc = now;
                link.RefreshFailures = link.RefreshFailures + 1;
                link.NextRefreshUtc = LinkRefreshSchedule.Backoff(now, link.RefreshFailures, interval);
                result.Outcome = LinkRefreshOutcomeEnum.Failed;
                result.Message = e.Message;
                _Logging.Debug(_Header + "check of " + link.Url + " failed (" + link.RefreshFailures + " in a row): " + e.Message);
            }
            await SaveAsync(link, result, token).ConfigureAwait(false);
            return result;
        }

        private async Task SaveAsync(SubjectLink link, LinkRefreshResult result, CancellationToken token)
        {
            await _Db.SubjectLinks.UpdateRefreshStateAsync(link, CancellationToken.None).ConfigureAwait(false);
            result.NextRefreshUtc = link.NextRefreshUtc;
            PneumaMetrics.RecordLinkRefresh(result.Outcome.ToString());
        }

        #endregion
    }
}
