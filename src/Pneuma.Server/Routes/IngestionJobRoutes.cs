namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
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
        private readonly CascadeDeletionService _Cascade;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate ingestion job routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="cascade">Cascade deletion service, used to remove a job's subordinate objects.</param>
        public IngestionJobRoutes(DatabaseDriverBase db, AuthorizationService authz, CascadeDeletionService cascade)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (cascade == null) throw new ArgumentNullException(nameof(cascade));
            _Db = db;
            _Authz = authz;
            _Cascade = cascade;
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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/jobs/{id}", DetailAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get ingestion job detail with events", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/jobs/{id}/restart", RestartAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Restart a failed ingestion job", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/jobs/{id}/stop", StopAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Stop (cancel) an ingestion job", "Ingestion"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/jobs/{id}/log", LogAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get an ingestion job's live per-stage log", "Ingestion"));
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

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;

            IngestionStatusEnum? status = null;
            string? statusText = ctx.Request.Query.Elements?["status"];
            if (!String.IsNullOrEmpty(statusText) && Enum.TryParse<IngestionStatusEnum>(statusText, true, out IngestionStatusEnum parsed))
            {
                status = parsed;
            }

            List<IngestionJob> jobs = await _Db.IngestionJobs.EnumerateAsync(tenantId, status, ctx.Token).ConfigureAwait(false);
            EnumerationResult<IngestionJob> result = EnumerationHelper.Paginate(jobs, RouteHelper.ReadEnumerationQuery(ctx), j => j.CreatedUtc, j => j.SourceUrl);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
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

            // Cascade the job's downstream contributions (graph nodes/edges, indexed documents, raw blob,
            // processing log) then the job row. The per-link S3 artifacts belong to the link (shared across
            // its jobs) and are left intact — they are cascaded when the link itself is deleted.
            await _Cascade.DeleteJobCascadeAsync(tenantId, job, ctx.Token).ConfigureAwait(false);

            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
