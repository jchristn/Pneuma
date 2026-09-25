namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Ingestion job routes: list, detail with stage events, and restart of failed jobs.
    /// </summary>
    public class IngestionJobRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate ingestion job routes. Deletion is durable and background: the routes mark a job
        /// for deletion (persisted status) and the JobDeletionWorker performs the cascade.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public IngestionJobRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            _Db = db;
            _Authz = authz;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/jobs", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List ingestion jobs", "Ingestion"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/jobs/summary", SummaryAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Summarize ingestion activity by pipeline stage", "Ingestion"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/jobs/live", LiveAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Live ingestion snapshot: running, waiting-for-slot, and queued jobs", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/jobs/{id}", DetailAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get ingestion job detail with events", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/jobs/{id}/restart", RestartAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Restart a failed ingestion job", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/jobs/{id}/stop", StopAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Stop (cancel) an ingestion job", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/jobs/{id}/log", LogAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get an ingestion job's live per-stage log", "Ingestion"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/jobs/delete", BulkDeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete multiple ingestion jobs and their artifacts", "Ingestion").WithRequestBody(OpenApiBodies.Json<IdListRequest>("Delete multiple ingestion jobs and their artifacts")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/jobs/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete an ingestion job and its artifacts", "Ingestion"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.IngestionJob, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private static string? Q(System.Collections.Specialized.NameValueCollection? q, string key)
        {
            string? value = q?[key];
            return String.IsNullOrEmpty(value) ? null : System.Net.WebUtility.UrlDecode(value);
        }

        private static DateTime? ParseUtc(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return null;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;

            IngestionStatusEnum? status = null;
            string? statusText = RouteHelper.Query(ctx, "status");
            if (!String.IsNullOrEmpty(statusText) && Enum.TryParse<IngestionStatusEnum>(statusText, true, out IngestionStatusEnum parsed))
            {
                status = parsed;
            }

            List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateAsync(tenantId, status, ctx.Token).ConfigureAwait(false);

            // Optional subject filter, applied before pagination so the counts reflect the filtered set.
            string? subjectFilter = RouteHelper.Query(ctx, "subjectId");
            if (!String.IsNullOrEmpty(subjectFilter))
            {
                List<IngestionJob> filtered = new List<IngestionJob>();
                foreach (IngestionJob job in jobs)
                {
                    if (String.Equals(job.SubjectId, subjectFilter, StringComparison.Ordinal)) filtered.Add(job);
                }
                jobs = filtered;
            }

            EnumerationResult<IngestionJob> result = EnumerationHelper.Paginate(jobs, RouteHelper.ReadEnumerationQuery(ctx), j => j.CreatedUtc, j => j.SourceUrl);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task LiveAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectFilter = RouteHelper.Query(ctx, "subjectId");

            IngestionLiveSnapshot snapshot = new IngestionLiveSnapshot();

            // Jobs actively being processed: classify each as running its current stage, or waiting for a
            // concurrency slot at that stage (its latest event is still Queued). The active set is bounded by the
            // job-pool size, so reading each active job's events for its latest state is inexpensive.
            List<IngestionJob> processing = await _Db.IngestionJobs.EnumerateAsync(tenantId, IngestionStatusEnum.Processing, ctx.Token).ConfigureAwait(false);
            foreach (IngestionJob job in processing)
            {
                if (!MatchesSubject(job, subjectFilter)) continue;

                IngestionJobEvent? latest = await LatestEventAsync(tenantId, job.Id, ctx.Token).ConfigureAwait(false);
                if (latest != null && latest.Status == IngestionStatusEnum.Queued)
                {
                    // Still queued for the stage's concurrency slot: "time in state" is time spent waiting.
                    snapshot.WaitingForSlot.Add(ToLiveJob(job, latest.Stage, latest.CreatedUtc));
                }
                else if (latest != null && latest.Status == IngestionStatusEnum.Processing)
                {
                    // Actively running the stage. The event's QueueDurationMs is the time it waited for the slot
                    // (0 if it was uncontended), so running-since = the moment the slot was acquired.
                    snapshot.Running.Add(ToLiveJob(job, latest.Stage, latest.CreatedUtc.AddMilliseconds(latest.QueueDurationMs)));
                }
                else
                {
                    // No usable in-progress event yet (e.g. between stages) — fall back to the job's own stage/time.
                    snapshot.Running.Add(ToLiveJob(job, job.Stage, job.LastUpdateUtc));
                }
            }

            // Jobs still waiting in the pool to be claimed and started.
            List<IngestionJob> queued = await _Db.IngestionJobs.EnumerateAsync(tenantId, IngestionStatusEnum.Queued, ctx.Token).ConfigureAwait(false);
            foreach (IngestionJob job in queued)
            {
                if (!MatchesSubject(job, subjectFilter)) continue;
                snapshot.Queued.Add(ToLiveJob(job, null, job.CreatedUtc));
            }

            // Longest-in-state first, so the most contended work surfaces at the top of each list.
            snapshot.Running.Sort((a, b) => a.StateSinceUtc.CompareTo(b.StateSinceUtc));
            snapshot.WaitingForSlot.Sort((a, b) => a.StateSinceUtc.CompareTo(b.StateSinceUtc));
            snapshot.Queued.Sort((a, b) => a.StateSinceUtc.CompareTo(b.StateSinceUtc));

            await RouteHelper.SendJsonAsync(ctx, 200, snapshot).ConfigureAwait(false);
        }

        private static bool MatchesSubject(IngestionJob job, string? subjectFilter)
        {
            return String.IsNullOrEmpty(subjectFilter) || String.Equals(job.SubjectId, subjectFilter, StringComparison.Ordinal);
        }

        private static IngestionLiveJob ToLiveJob(IngestionJob job, IngestionStageEnum? stage, DateTime stateSinceUtc)
        {
            return new IngestionLiveJob
            {
                JobId = job.Id,
                SubjectId = job.SubjectId,
                SourceUrl = job.SourceUrl,
                Stage = stage,
                StateSinceUtc = stateSinceUtc
            };
        }

        private async Task<IngestionJobEvent?> LatestEventAsync(string tenantId, string jobId, CancellationToken token)
        {
            List<IngestionJobEvent> events = await _Db.IngestionJobEvents.EnumerateByJobAsync(tenantId, jobId, token).ConfigureAwait(false);
            IngestionJobEvent? latest = null;
            foreach (IngestionJobEvent jobEvent in events)
            {
                if (latest == null || jobEvent.CreatedUtc > latest.CreatedUtc) latest = jobEvent;
            }
            return latest;
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            System.Collections.Specialized.NameValueCollection? q = ctx.Request.Query.Elements;
            IngestionActivityFilter filter = new IngestionActivityFilter
            {
                TenantId = rc.TenantId,
                SubjectId = Q(q, "subjectId"),
                FromUtc = ParseUtc(Q(q, "fromUtc")),
                ToUtc = ParseUtc(Q(q, "toUtc"))
            };

            string? bucketMinutes = Q(q, "bucketMinutes");
            if (!String.IsNullOrEmpty(bucketMinutes) && Int32.TryParse(bucketMinutes, out int bm)) filter.BucketMinutes = bm;

            IngestionActivitySummary summary = await _Db.IngestionJobEvents.SummarizeAsync(filter, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, summary).ConfigureAwait(false);
        }

        private async Task DetailAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string id = RouteHelper.Param(ctx, "id");

            IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Job not found.").ConfigureAwait(false);
                return;
            }

            List<IngestionJobEvent> events = await _Db.IngestionJobEvents.EnumerateByJobAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            IngestionJobDetail detail = new IngestionJobDetail { Job = job, Events = events };
            await RouteHelper.SendJsonAsync(ctx, 200, detail).ConfigureAwait(false);
        }

        private async Task RestartAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string id = RouteHelper.Param(ctx, "id");

            IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Job not found.").ConfigureAwait(false);
                return;
            }

            job.Status = IngestionStatusEnum.Queued;
            job.Stage = IngestionStageEnum.Pending;
            job.Error = null;
            job.CompletedUtc = null;
            job.StartedUtc = null;
            IngestionJob updated = await _Db.IngestionJobs.UpdateAsync(job, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
        }

        private async Task StopAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string id = RouteHelper.Param(ctx, "id");

            IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Job not found.").ConfigureAwait(false);
                return;
            }

            if (job.Status == IngestionStatusEnum.Completed ||
                job.Status == IngestionStatusEnum.Failed ||
                job.Status == IngestionStatusEnum.Cancelled)
            {
                await RouteHelper.SendErrorAsync(ctx, 409, "Conflict", "Job has already finished and cannot be stopped.").ConfigureAwait(false);
                return;
            }

            job.Status = IngestionStatusEnum.Cancelled;
            job.CompletedUtc = DateTime.UtcNow;
            job.Error = "Cancelled by operator.";
            IngestionJob updated = await _Db.IngestionJobs.UpdateAsync(job, ctx.Token).ConfigureAwait(false);

            IngestionJobEvent stopEvent = new IngestionJobEvent
            {
                TenantId = tenantId,
                JobId = job.Id,
                SubjectId = job.SubjectId,
                Stage = job.Stage,
                Status = IngestionStatusEnum.Cancelled,
                Message = "Job cancelled by operator."
            };
            await _Db.IngestionJobEvents.CreateAsync(stopEvent, ctx.Token).ConfigureAwait(false);

            await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
        }

        private async Task LogAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string id = RouteHelper.Param(ctx, "id");

            IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Job not found.").ConfigureAwait(false);
                return;
            }

            List<IngestionJobEvent> events = await _Db.IngestionJobEvents.EnumerateByJobAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            IngestionJobDetail detail = new IngestionJobDetail { Job = job, Events = events };
            await RouteHelper.SendJsonAsync(ctx, 200, detail).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string id = RouteHelper.Param(ctx, "id");

            IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Job not found.").ConfigureAwait(false);
                return;
            }

            // Mark the job for durable background deletion (a persisted status the JobDeletionWorker acts on) and
            // cancel it so any in-flight processing stops at its next stage boundary. The heavy cascade (graph,
            // index documents, blob, events, job row) then runs asynchronously — the caller is not held. 202.
            await MarkForDeletionAsync(job, ctx.Token).ConfigureAwait(false);

            ctx.Response.StatusCode = 202;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task BulkDeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;

            IdListRequest? request = RouteHelper.ReadBody<IdListRequest>(ctx);
            if (request == null || request.Ids == null || request.Ids.Count == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A non-empty list of job ids is required.").ConfigureAwait(false);
                return;
            }

            // Mark each job for durable background deletion in this single request (no browser fan-out); the
            // JobDeletionWorker performs the cascades. 202 Accepted is returned immediately.
            int accepted = 0;
            foreach (string id in request.Ids)
            {
                if (String.IsNullOrWhiteSpace(id)) continue;
                IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
                if (job == null) continue;
                await MarkForDeletionAsync(job, ctx.Token).ConfigureAwait(false);
                accepted++;
            }

            await RouteHelper.SendJsonAsync(ctx, 202, new { accepted = accepted }).ConfigureAwait(false);
        }

        // Mark a job for background cascade deletion: set DeletionStatus=Pending and cancel it so in-flight
        // processing stops at the next stage boundary. No-op when already Pending/Deleting so a re-request does
        // not reset progress.
        private async Task MarkForDeletionAsync(IngestionJob job, CancellationToken token)
        {
            if (job.DeletionStatus == JobDeletionStatusEnum.Pending || job.DeletionStatus == JobDeletionStatusEnum.Deleting) return;
            job.DeletionStatus = JobDeletionStatusEnum.Pending;
            job.Status = IngestionStatusEnum.Cancelled;
            await _Db.IngestionJobs.UpdateAsync(job, token).ConfigureAwait(false);
        }

        #endregion
    }
}
