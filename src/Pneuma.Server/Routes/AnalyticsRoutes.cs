namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Per-subject chat analytics routes: a single windowed report of volume, latency percentiles, per-stage
    /// timing, and feedback for the dashboard Analytics view.
    /// </summary>
    public class AnalyticsRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly AnalyticsService _Analytics;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate analytics routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public AnalyticsRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Analytics = new AnalyticsService(db);
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/analytics", GetAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Per-subject chat analytics", "Analytics"));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            int days = 30;
            string? daysRaw = ctx.Request.Query.Elements?["days"];
            if (!String.IsNullOrEmpty(daysRaw) && Int32.TryParse(daysRaw, out int parsed)) days = Math.Clamp(parsed, 1, 365);
            DateTime sinceUtc = DateTime.UtcNow.AddDays(-days);
            AnalyticsReport report = await _Analytics.BuildAsync(rc.TenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, sinceUtc, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, report).ConfigureAwait(false);
        }

        #endregion
    }
}
