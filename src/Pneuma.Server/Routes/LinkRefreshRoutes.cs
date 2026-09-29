namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Refresh;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Scheduled link refresh routes: set one link's refresh interval, set it for several links, and check a link now.
    /// Only URL links that no crawl plan owns can be refreshed.
    /// </summary>
    public class LinkRefreshRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly LinkRefreshService _Refresh;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="refresh">Refresh service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public LinkRefreshRoutes(DatabaseDriverBase db, AuthorizationService authz, LinkRefreshService refresh)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Refresh = refresh ?? throw new ArgumentNullException(nameof(refresh));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/links/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Set a link's refresh interval (0 off, 60 to 525600 minutes, or useSubjectDefault)", "Subjects").WithRequestBody(OpenApiBodies.Json<LinkRefreshRequest>("The refresh interval")));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/links/refresh-interval", BulkAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Set the refresh interval of several links", "Subjects").WithRequestBody(OpenApiBodies.Json<LinkRefreshRequest>("Link ids and the refresh interval")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/links/{id}/refresh", RefreshNowAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Check a link for changes now (conditional GET); queues a re-ingest when it changed", "Subjects"));
        }

        #endregion

        #region Private-Methods

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            LinkRefreshRequest? request = RouteHelper.ReadBody<LinkRefreshRequest>(ctx);
            if (!await ValidateAsync(ctx, request).ConfigureAwait(false)) return;

            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }
            string? problem = RefreshProblem(link);
            if (problem != null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", problem).ConfigureAwait(false);
                return;
            }
            await ApplyAsync(link, request!, ctx.Token).ConfigureAwait(false);
            SubjectLink? saved = await _Db.SubjectLinks.ReadAsync(link.TenantId, link.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task BulkAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            LinkRefreshRequest? request = RouteHelper.ReadBody<LinkRefreshRequest>(ctx);
            if (!await ValidateAsync(ctx, request).ConfigureAwait(false)) return;
            if (request!.Ids == null || request.Ids.Count == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A non-empty list of link ids is required.").ConfigureAwait(false);
                return;
            }

            int updated = 0;
            List<string> skipped = new List<string>();
            foreach (string id in request.Ids.Where(i => !String.IsNullOrWhiteSpace(i)).Distinct(StringComparer.Ordinal))
            {
                SubjectLink? link = await _Db.SubjectLinks.ReadAsync(rc.TenantId ?? String.Empty, id, ctx.Token).ConfigureAwait(false);
                if (link == null || RefreshProblem(link) != null)
                {
                    skipped.Add(id);
                    continue;
                }
                await ApplyAsync(link, request, ctx.Token).ConfigureAwait(false);
                updated++;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, new LinkRefreshBulkResult { Updated = updated, Skipped = skipped }).ConfigureAwait(false);
        }

        private async Task RefreshNowAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            SubjectLink? link = await _Db.SubjectLinks.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (link == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Link not found.").ConfigureAwait(false);
                return;
            }
            string? problem = RefreshProblem(link);
            if (problem != null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", problem).ConfigureAwait(false);
                return;
            }
            LinkRefreshResult result = await _Refresh.RefreshNowAsync(link, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task ApplyAsync(SubjectLink link, LinkRefreshRequest request, System.Threading.CancellationToken token)
        {
            link.RefreshIntervalMinutes = request.UseSubjectDefault ? null : request.RefreshIntervalMinutes;
            Subject? subject = await _Db.Subjects.ReadAsync(link.TenantId, link.SubjectId, token).ConfigureAwait(false);
            LinkRefreshService.Reschedule(link, subject);
            await _Db.SubjectLinks.UpdateRefreshStateAsync(link, token).ConfigureAwait(false);
        }

        private static string? RefreshProblem(SubjectLink link)
        {
            if (link.SourceKind != SourceKindEnum.Url) return "Only URL links can be refreshed; pushed content is replaced by pushing it again.";
            if (!String.IsNullOrEmpty(link.CrawlPlanId)) return "This link is managed by a crawl plan; its plan's schedule refreshes it.";
            if (link.DeletionStatus != LinkDeletionStatusEnum.None) return "This link is being deleted.";
            return null;
        }

        private static async Task<bool> ValidateAsync(HttpContextBase ctx, LinkRefreshRequest? request)
        {
            if (request == null || (!request.UseSubjectDefault && request.RefreshIntervalMinutes == null))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Set refreshIntervalMinutes (0 for off) or useSubjectDefault.").ConfigureAwait(false);
                return false;
            }
            string? problem = request.UseSubjectDefault ? null : LinkRefreshSchedule.Validate(request.RefreshIntervalMinutes, "refreshIntervalMinutes");
            if (problem != null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", problem).ConfigureAwait(false);
                return false;
            }
            return true;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Write, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        #endregion
    }
}
