namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Crawl plan routes: the type catalog, plan CRUD, connectivity tests, previews, and start and stop. Secrets are
    /// write-only. Operations and tracked objects are in <see cref="CrawlOperationRoutes"/>.
    /// </summary>
    public class CrawlPlanRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly ICollectionStore _Collections;
        private readonly CrawlerFactory _Crawlers;
        private readonly CrawlPlanService _Plans;
        private readonly CrawlSyncService _Sync;
        private readonly CrawlSchedulerService _Scheduler;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="collections">Collection store (subject configuration check).</param>
        /// <param name="crawlers">Registered crawlers.</param>
        /// <param name="plans">Plan service.</param>
        /// <param name="sync">Sync service (previews).</param>
        /// <param name="scheduler">Scheduler.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlPlanRoutes(DatabaseDriverBase db, AuthorizationService authz, ICollectionStore collections, CrawlerFactory crawlers, CrawlPlanService plans, CrawlSyncService sync, CrawlSchedulerService scheduler)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _Crawlers = crawlers ?? throw new ArgumentNullException(nameof(crawlers));
            _Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _Sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            const string tag = "Crawling";

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/crawl-plan-types", TypesAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List the crawl plan types this server supports, each with its settings schema", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{subjectId}/crawl-plans", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a crawl plan that keeps a subject in sync with a source", tag).WithRequestBody(OpenApiBodies.Json<CrawlPlanRequest>("The crawl plan")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{subjectId}/crawl-plans", ListBySubjectAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's crawl plans", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/crawl-plans", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List crawl plans (optionally ?subjectId=)", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/crawl-plans/test", TestDraftAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Test a draft crawl plan's connection without saving it (?fromPlanId= fills unchanged secrets from a stored plan)", tag).WithRequestBody(OpenApiBodies.Json<CrawlPlanRequest>("The draft plan")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/crawl-plans/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a crawl plan (secrets are never returned)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/crawl-plans/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Replace a crawl plan's configuration (empty secrets keep their stored values)", tag).WithRequestBody(OpenApiBodies.Json<CrawlPlanRequest>("The crawl plan")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/crawl-plans/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a crawl plan (?deleteLinks=true also deletes the links it created)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/crawl-plans/{id}/test", TestAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Test a crawl plan's connection step by step", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/crawl-plans/{id}/preview", PreviewAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Preview what a crawl plan would add, update, and delete if it ran now (nothing is changed)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/crawl-plans/{id}/start", StartAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Start a crawl operation now", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/crawl-plans/{id}/stop", StopAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Stop a crawl plan's running operation", tag));
        }

        #endregion

        #region Private-Methods

        private async Task TypesAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            await RouteHelper.SendJsonAsync(ctx, 200, _Crawlers.Catalog()).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");

            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }
            if (!await RequireSubjectConfiguredAsync(ctx, tenantId, subject).ConfigureAwait(false)) return;

            CrawlPlanRequest? request = RouteHelper.ReadBody<CrawlPlanRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A crawl plan body is required.").ConfigureAwait(false);
                return;
            }
            CrawlPlan plan = request.ToPlan(tenantId, subjectId);
            CrawlPlanService.Clean(plan);
            List<string> errors = _Plans.Validate(plan, null);
            if (errors.Count > 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", String.Join(" ", errors)).ConfigureAwait(false);
                return;
            }
            if (!await AllowRobotsChangeAsync(ctx, rc, null, plan).ConfigureAwait(false)) return;

            bool robotsOff = plan.Web != null && !plan.Web.RespectRobotsTxt;
            CrawlPlanSaveResult result = await _Plans.CreateAsync(plan, ctx.Token).ConfigureAwait(false);
            await AuditSecurityAsync(ctx, rc, result.Plan.Id, result.ChangedSecrets, robotsOff ? "robots.txt disabled" : null).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, result.Plan).ConfigureAwait(false);
        }

        private async Task ListBySubjectAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }
            List<CrawlPlan> plans = await _Db.CrawlPlans.EnumerateBySubjectAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(plans, RouteHelper.ReadEnumerationQuery(ctx), p => p.CreatedUtc, p => p.Name)).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectId = RouteHelper.Query(ctx, "subjectId");
            List<CrawlPlan> plans = String.IsNullOrWhiteSpace(subjectId)
                ? await _Db.CrawlPlans.EnumerateAsync(tenantId, ctx.Token).ConfigureAwait(false)
                : await _Db.CrawlPlans.EnumerateBySubjectAsync(tenantId, subjectId!, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(plans, RouteHelper.ReadEnumerationQuery(ctx), p => p.CreatedUtc, p => p.Name)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            CrawlPlan? plan = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (plan == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, plan).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Write).ConfigureAwait(false)) return;
            CrawlPlan? existing = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (existing == null) return;

            CrawlPlanRequest? request = RouteHelper.ReadBody<CrawlPlanRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A crawl plan body is required.").ConfigureAwait(false);
                return;
            }
            if (request.Type != existing.Type)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A crawl plan's type cannot change; create a new plan instead.").ConfigureAwait(false);
                return;
            }

            CrawlPlan incoming = request.ToPlan(existing.TenantId, existing.SubjectId);
            CrawlPlanService.Clean(incoming);
            List<string> remaining = existing.SecretsSet.Where(s => request.ClearSecrets == null || !request.ClearSecrets.Contains(s, StringComparer.Ordinal)).ToList();
            List<string> errors = _Plans.Validate(incoming, remaining);
            if (errors.Count > 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", String.Join(" ", errors)).ConfigureAwait(false);
                return;
            }
            if (!await AllowRobotsChangeAsync(ctx, rc, existing, incoming).ConfigureAwait(false)) return;

            bool robotsTurnedOff = existing.Web != null && existing.Web.RespectRobotsTxt && incoming.Web != null && !incoming.Web.RespectRobotsTxt;
            CrawlPlanSaveResult result = await _Plans.UpdateAsync(existing, incoming, request.ClearSecrets, ctx.Token).ConfigureAwait(false);
            await AuditSecurityAsync(ctx, rc, result.Plan.Id, result.ChangedSecrets, robotsTurnedOff ? "robots.txt disabled" : null).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result.Plan).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            CrawlPlan? plan = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (plan == null) return;
            if (plan.Status != CrawlPlanStatusEnum.Idle)
            {
                await RouteHelper.SendErrorAsync(ctx, 409, "Conflict", "The crawl plan is running; stop it before deleting it.").ConfigureAwait(false);
                return;
            }

            bool deleteLinks = String.Equals(RouteHelper.Query(ctx, "deleteLinks"), "true", StringComparison.OrdinalIgnoreCase);
            List<SubjectLink> links = await _Db.SubjectLinks.EnumerateByCrawlPlanAsync(plan.TenantId, plan.Id, ctx.Token).ConfigureAwait(false);
            int linksDeleted = 0;
            foreach (SubjectLink link in links)
            {
                if (deleteLinks)
                {
                    if (link.DeletionStatus != LinkDeletionStatusEnum.None) continue;
                    link.DeletionStatus = LinkDeletionStatusEnum.Pending;
                    linksDeleted++;
                }
                else
                {
                    // Kept links stay searchable. A link with a web address can still be re-ingested by URL; a link
                    // from a bucket or share cannot be re-read without its plan.
                    link.CrawlPlanId = null;
                    if (link.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || link.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        link.SourceKind = SourceKindEnum.Url;
                }
                await _Db.SubjectLinks.UpdateAsync(link, ctx.Token).ConfigureAwait(false);
            }

            await _Db.CrawlPlans.DeleteAsync(plan.TenantId, plan.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new CrawlPlanDeleteResult { Deleted = true, LinksDeleted = linksDeleted, LinksKept = deleteLinks ? 0 : links.Count }).ConfigureAwait(false);
        }

        private async Task TestDraftAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlPlanRequest? request = RouteHelper.ReadBody<CrawlPlanRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A crawl plan body is required.").ConfigureAwait(false);
                return;
            }
            if (!_Crawlers.IsAvailable(request.Type))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "No crawler is available for " + request.Type + " plans on this server.").ConfigureAwait(false);
                return;
            }
            CrawlPlan draft = request.ToPlan(rc.TenantId ?? String.Empty, String.Empty);
            if (draft.SettingsObject() == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", CrawlSettingsCodec.SettingsProperty(draft.Type) + " settings are required for a " + draft.Type + " plan.").ConfigureAwait(false);
                return;
            }
            string? fromPlanId = RouteHelper.Query(ctx, "fromPlanId");
            if (!String.IsNullOrWhiteSpace(fromPlanId)) await _Plans.FillMissingSecretsAsync(draft, fromPlanId!, ctx.Token).ConfigureAwait(false);

            ConnectivityResult result = await _Crawlers.Get(draft.Type).TestAsync(draft, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlPlan? plan = await _Plans.ReadWithSecretsAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (plan == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Crawl plan not found.").ConfigureAwait(false);
                return;
            }
            if (!await RequireCrawlerAsync(ctx, plan).ConfigureAwait(false)) return;
            ConnectivityResult result = await _Crawlers.Get(plan.Type).TestAsync(plan, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task PreviewAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlPlan? plan = await _Plans.ReadWithSecretsAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (plan == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Crawl plan not found.").ConfigureAwait(false);
                return;
            }
            if (!await RequireCrawlerAsync(ctx, plan).ConfigureAwait(false)) return;
            CrawlPreview preview;
            try
            {
                preview = await _Sync.PreviewAsync(plan, true, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                await RouteHelper.SendErrorAsync(ctx, 502, "BadGateway", "The source could not be listed: " + e.Message).ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, preview).ConfigureAwait(false);
        }

        private async Task StartAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlPlan? plan = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (plan == null) return;
            if (!await RequireCrawlerAsync(ctx, plan).ConfigureAwait(false)) return;
            Subject? subject = await _Db.Subjects.ReadAsync(plan.TenantId, plan.SubjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "The crawl plan's subject no longer exists.").ConfigureAwait(false);
                return;
            }
            if (!await RequireSubjectConfiguredAsync(ctx, plan.TenantId, subject).ConfigureAwait(false)) return;

            CrawlStartResult result = await _Scheduler.StartAsync(plan.TenantId, plan.Id, CrawlTriggerEnum.Manual, ctx.Token).ConfigureAwait(false);
            await SendStartResultAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task StopAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlStartResult result = await _Scheduler.StopAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await SendStartResultAsync(ctx, result).ConfigureAwait(false);
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, ResourceTypeEnum resource, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, resource, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task<CrawlPlan?> ReadPlanOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            CrawlPlan? plan = await _Db.CrawlPlans.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (plan == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Crawl plan not found.").ConfigureAwait(false);
            return plan;
        }

        private async Task<bool> RequireCrawlerAsync(HttpContextBase ctx, CrawlPlan plan)
        {
            if (_Crawlers.IsAvailable(plan.Type)) return true;
            await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "No crawler is available for " + plan.Type + " plans on this server.").ConfigureAwait(false);
            return false;
        }

        private async Task<bool> RequireSubjectConfiguredAsync(HttpContextBase ctx, string tenantId, Subject subject)
        {
            if (String.IsNullOrWhiteSpace(subject.EmbeddingModel) || String.IsNullOrWhiteSpace(subject.InferenceModel))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject has no embedding and inference model configured. Set them on the subject before crawling into it.").ConfigureAwait(false);
                return false;
            }
            if (String.IsNullOrWhiteSpace(subject.Collection))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject has no collection configured. Set it on the subject before crawling into it.").ConfigureAwait(false);
                return false;
            }
            if (!await _Collections.CollectionExistsAsync(tenantId, subject.Collection!, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "This subject's configured collection no longer exists. Update the subject's collection.").ConfigureAwait(false);
                return false;
            }
            return true;
        }

        private static async Task SendStartResultAsync(HttpContextBase ctx, CrawlStartResult result)
        {
            if (result.StatusCode >= 400)
            {
                string code = result.StatusCode == 404 ? "NotFound" : result.StatusCode == 409 ? "Conflict" : "BadRequest";
                await RouteHelper.SendErrorAsync(ctx, result.StatusCode, code, result.Error ?? "Request refused.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, result.StatusCode, result.Operation).ConfigureAwait(false);
        }

        // Turning robots.txt off is limited to system and tenant administrators; a refusal is audited.
        private async Task<bool> AllowRobotsChangeAsync(HttpContextBase ctx, RequestContext rc, CrawlPlan? existing, CrawlPlan incoming)
        {
            if (!CrawlPlanService.TurnsOffRobots(existing, incoming) || rc.IsAdmin || rc.IsTenantAdmin) return true;
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.AuthorizationDenied,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    ResourceId = existing?.Id,
                    RequiredResourceType = ResourceTypeEnum.CrawlPlan,
                    RequiredOperation = OperationTypeEnum.Write,
                    HttpMethod = ctx.Request.Method.ToString(),
                    UrlPath = ctx.Request.Url.RawWithoutQuery,
                    SourceIp = ctx.Request.Source?.IpAddress,
                    DenialReason = "Turning off robots.txt requires an administrator."
                };
                await _Db.Audit.CreateAsync(record, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // Audit is best-effort; the refusal stands either way.
            }
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Only an administrator can turn off robots.txt for a crawl plan.").ConfigureAwait(false);
            return false;
        }

        private async Task AuditSecurityAsync(HttpContextBase ctx, RequestContext rc, string planId, List<string> changedSecrets, string? note)
        {
            if (changedSecrets.Count == 0 && note == null) return;
            List<string> parts = new List<string>();
            if (changedSecrets.Count > 0) parts.Add("secrets changed: " + String.Join(", ", changedSecrets));
            if (note != null) parts.Add(note);
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.CrawlPlanSecurityChanged,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    ResourceId = planId,
                    RequiredResourceType = ResourceTypeEnum.CrawlPlan,
                    RequiredOperation = OperationTypeEnum.Write,
                    HttpMethod = ctx.Request.Method.ToString(),
                    UrlPath = ctx.Request.Url.RawWithoutQuery,
                    SourceIp = ctx.Request.Source?.IpAddress,
                    DenialReason = String.Join("; ", parts)
                };
                await _Db.Audit.CreateAsync(record, ctx.Token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // Audit is best-effort; the change stands either way.
            }
        }

        #endregion
    }
}
