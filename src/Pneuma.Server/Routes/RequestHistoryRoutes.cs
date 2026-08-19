namespace Pneuma.Server.Routes
{
    using System;
    using System.Globalization;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Request history routes at /v1.0/api/request-history. Tenant-scoped from the request context;
    /// a global administrator may widen scope with ?tenantId=.
    /// </summary>
    public class RequestHistoryRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate request history routes.</summary>
        /// <param name="db">Database driver.</param>
        public RequestHistoryRoutes(DatabaseDriverBase db)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _Db = db;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/request-history", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List request history", "RequestHistory"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/api/request-history/summary", SummaryAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Summarize request history", "RequestHistory"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/api/request-history/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a request history entry", "RequestHistory"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/api/request-history/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a request history entry", "RequestHistory"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.DELETE, "/v1.0/api/request-history", DeleteManyAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Bulk delete request history", "RequestHistory"));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestHistoryFilter filter = BuildFilter(ctx);
            RequestHistoryPage page = await _Db.RequestHistory.EnumerateAsync(filter, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, page).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase ctx)
        {
            RequestHistoryFilter filter = BuildFilter(ctx);
            RequestHistorySummary summary = await _Db.RequestHistory.SummarizeAsync(filter, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, summary).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            string id = RouteHelper.Param(ctx, "id");
            string? scope = rc.IsAdmin ? null : rc.TenantId;
            RequestHistoryEntry? entry = await _Db.RequestHistory.ReadAsync(scope, id, ctx.Token).ConfigureAwait(false);
            if (entry == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Request history entry not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, entry).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            string id = RouteHelper.Param(ctx, "id");
            string? scope = rc.IsAdmin ? null : rc.TenantId;
            bool deleted = await _Db.RequestHistory.DeleteAsync(scope, id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Request history entry not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase ctx)
        {
            RequestHistoryFilter filter = BuildFilter(ctx);
            int count = await _Db.RequestHistory.DeleteManyAsync(filter, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new DeletedCountResponse { DeletedCount = count }).ConfigureAwait(false);
        }

        private RequestHistoryFilter BuildFilter(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            System.Collections.Specialized.NameValueCollection? q = ctx.Request.Query.Elements;

            RequestHistoryFilter filter = new RequestHistoryFilter();

            // Tenant scope: non-admins are pinned to their own tenant; admins may widen via ?tenantId.
            if (rc.IsAdmin)
            {
                filter.TenantId = Q(q, "tenantId");
            }
            else
            {
                filter.TenantId = rc.TenantId;
            }

            filter.UserId = Q(q, "userId");
            filter.Method = Q(q, "method");
            filter.PathContains = Q(q, "pathContains");

            string? statusCode = Q(q, "statusCode");
            if (!String.IsNullOrEmpty(statusCode) && Int32.TryParse(statusCode, out int sc)) filter.StatusCode = sc;

            filter.FromUtc = ParseUtc(Q(q, "fromUtc"));
            filter.ToUtc = ParseUtc(Q(q, "toUtc"));

            string? pageNumber = Q(q, "pageNumber");
            if (!String.IsNullOrEmpty(pageNumber) && Int32.TryParse(pageNumber, out int pn)) filter.PageNumber = pn;

            string? pageSize = Q(q, "pageSize");
            if (!String.IsNullOrEmpty(pageSize) && Int32.TryParse(pageSize, out int ps)) filter.PageSize = ps;

            string? bucketMinutes = Q(q, "bucketMinutes");
            if (!String.IsNullOrEmpty(bucketMinutes) && Int32.TryParse(bucketMinutes, out int bm)) filter.BucketMinutes = bm;

            return filter;
        }

        private static string? Q(System.Collections.Specialized.NameValueCollection? q, string key)
        {
            string? value = q?[key];
            return String.IsNullOrEmpty(value) ? null : value;
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

        #endregion
    }
}
