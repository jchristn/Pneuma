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
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Crawl operation routes: a plan's operations and tracked objects, the tenant's operations, one operation with
    /// its per-object results, and confirming the deletions a held operation is waiting on.
    /// </summary>
    public class CrawlOperationRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly CrawlSyncService _Sync;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="sync">Sync service (confirming deletions).</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public CrawlOperationRoutes(DatabaseDriverBase db, AuthorizationService authz, CrawlSyncService sync)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Sync = sync ?? throw new ArgumentNullException(nameof(sync));
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

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/crawl-plans/{id}/operations", ListPlanOperationsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a crawl plan's operations, newest first", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/crawl-plans/{id}/objects", ListObjectsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List the objects a crawl plan tracks (optionally ?status=)", tag));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/crawl-operations", ListOperationsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List crawl operations (optionally ?planId= and ?status=)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/crawl-operations/{id}", ReadOperationAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a crawl operation", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/crawl-operations/{id}/objects", ListOperationObjectsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List what a crawl operation did with each object (optionally ?action=)", tag));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/crawl-operations/{id}/confirm-deletions", ConfirmDeletionsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Confirm the deletions a held crawl operation is waiting on", tag));
        }

        #endregion

        #region Private-Methods

        private async Task ListPlanOperationsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            CrawlPlan? plan = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (plan == null) return;
            List<CrawlOperation> operations = await _Db.CrawlOperations.EnumerateByPlanAsync(plan.TenantId, plan.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(operations, RouteHelper.ReadEnumerationQuery(ctx), o => o.StartedUtc, o => o.Status.ToString())).ConfigureAwait(false);
        }

        private async Task ListObjectsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlPlan, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            CrawlPlan? plan = await ReadPlanOr404Async(ctx, rc).ConfigureAwait(false);
            if (plan == null) return;

            List<CrawlObject> objects = await _Db.CrawlObjects.EnumerateByPlanAsync(plan.TenantId, plan.Id, ctx.Token).ConfigureAwait(false);
            string? status = RouteHelper.Query(ctx, "status");
            if (!String.IsNullOrWhiteSpace(status))
            {
                CrawlObjectStatusEnum parsed;
                if (!Enum.TryParse(status, true, out parsed) || !Enum.IsDefined(typeof(CrawlObjectStatusEnum), parsed))
                {
                    await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "status must be one of " + String.Join(", ", Enum.GetNames(typeof(CrawlObjectStatusEnum))) + ".").ConfigureAwait(false);
                    return;
                }
                objects = objects.Where(o => o.Status == parsed).ToList();
            }
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(objects, RouteHelper.ReadEnumerationQuery(ctx), o => o.FirstSeenUtc, o => o.ExternalKey)).ConfigureAwait(false);
        }

        private async Task ListOperationsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = rc.TenantId ?? String.Empty;
            string? planId = RouteHelper.Query(ctx, "planId");
            List<CrawlOperation> operations = String.IsNullOrWhiteSpace(planId)
                ? await _Db.CrawlOperations.EnumerateAsync(tenantId, ctx.Token).ConfigureAwait(false)
                : await _Db.CrawlOperations.EnumerateByPlanAsync(tenantId, planId!, ctx.Token).ConfigureAwait(false);

            string? status = RouteHelper.Query(ctx, "status");
            if (!String.IsNullOrWhiteSpace(status))
            {
                CrawlOperationStatusEnum parsed;
                if (!Enum.TryParse(status, true, out parsed) || !Enum.IsDefined(typeof(CrawlOperationStatusEnum), parsed))
                {
                    await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "status must be one of " + String.Join(", ", Enum.GetNames(typeof(CrawlOperationStatusEnum))) + ".").ConfigureAwait(false);
                    return;
                }
                operations = operations.Where(o => o.Status == parsed).ToList();
            }
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(operations, RouteHelper.ReadEnumerationQuery(ctx), o => o.StartedUtc, o => o.PlanId)).ConfigureAwait(false);
        }

        private async Task ReadOperationAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            CrawlOperation? operation = await ReadOperationOr404Async(ctx, rc).ConfigureAwait(false);
            if (operation == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, operation).ConfigureAwait(false);
        }

        private async Task ListOperationObjectsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            CrawlOperation? operation = await ReadOperationOr404Async(ctx, rc).ConfigureAwait(false);
            if (operation == null) return;

            List<CrawlOperationObject> objects = await _Db.CrawlOperations.EnumerateObjectsAsync(operation.TenantId, operation.Id, ctx.Token).ConfigureAwait(false);
            string? action = RouteHelper.Query(ctx, "action");
            if (!String.IsNullOrWhiteSpace(action))
            {
                CrawlActionEnum parsed;
                if (!Enum.TryParse(action, true, out parsed) || !Enum.IsDefined(typeof(CrawlActionEnum), parsed))
                {
                    await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "action must be one of " + String.Join(", ", Enum.GetNames(typeof(CrawlActionEnum))) + ".").ConfigureAwait(false);
                    return;
                }
                objects = objects.Where(o => o.Action == parsed).ToList();
            }
            await RouteHelper.SendJsonAsync(ctx, 200, EnumerationHelper.Paginate(objects, RouteHelper.ReadEnumerationQuery(ctx), o => o.CreatedUtc, o => o.ExternalKey)).ConfigureAwait(false);
        }

        private async Task ConfirmDeletionsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, ResourceTypeEnum.CrawlOperation, OperationTypeEnum.Execute).ConfigureAwait(false)) return;
            CrawlOperation? operation = await ReadOperationOr404Async(ctx, rc).ConfigureAwait(false);
            if (operation == null) return;
            if (operation.Status != CrawlOperationStatusEnum.Held)
            {
                await RouteHelper.SendErrorAsync(ctx, 409, "Conflict", "Only a Held operation has deletions to confirm; this one is " + operation.Status + ".").ConfigureAwait(false);
                return;
            }
            await _Sync.ConfirmDeletionsAsync(operation, ctx.Token).ConfigureAwait(false);
            CrawlOperation? updated = await _Db.CrawlOperations.ReadAsync(operation.TenantId, operation.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
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

        private async Task<CrawlOperation?> ReadOperationOr404Async(HttpContextBase ctx, RequestContext rc)
        {
            CrawlOperation? operation = await _Db.CrawlOperations.ReadAsync(rc.TenantId ?? String.Empty, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (operation == null) await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Crawl operation not found.").ConfigureAwait(false);
            return operation;
        }

        #endregion
    }
}
