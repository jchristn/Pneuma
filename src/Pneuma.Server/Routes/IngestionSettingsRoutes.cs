namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
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
    /// System-wide ingestion tuning routes (per-stage concurrency caps, job pool, summarization tuning, stage
    /// timeout). Reading and writing are restricted to the system administrator. A write persists the singleton
    /// tuning row and live-applies it to the running pipeline (no restart), via the concurrency manager.
    /// </summary>
    public class IngestionSettingsRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly ConcurrencyManager _Concurrency;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate ingestion settings routes.</summary>
        /// <param name="db">Database driver (persists the tuning singleton).</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="concurrency">Runtime concurrency manager (live-applies changes).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public IngestionSettingsRoutes(DatabaseDriverBase db, AuthorizationService authz, ConcurrencyManager concurrency)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (concurrency == null) throw new ArgumentNullException(nameof(concurrency));
            _Db = db;
            _Authz = authz;
            _Concurrency = concurrency;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/settings/ingestion", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read ingestion concurrency tuning (system defaults)", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/v1.0/settings/ingestion", WriteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update ingestion concurrency tuning (applied live)", "Settings"));
        }

        #endregion

        #region Private-Methods

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!rc.IsAdmin)
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Ingestion tuning is restricted to the system administrator.").ConfigureAwait(false);
                return;
            }

            await RouteHelper.SendJsonAsync(ctx, 200, _Concurrency.CurrentDefaults()).ConfigureAwait(false);
        }

        private async Task WriteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!rc.IsAdmin)
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Ingestion tuning is restricted to the system administrator.").ConfigureAwait(false);
                return;
            }

            IngestionTuning? request = RouteHelper.ReadBody<IngestionTuning>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A tuning body is required.").ConfigureAwait(false);
                return;
            }

            // The model's property setters clamp every value; persist the singleton, then apply it live.
            IngestionTuning saved = await _Db.IngestionTuning.UpsertAsync(request, ctx.Token).ConfigureAwait(false);
            _Concurrency.ApplySystemDefaults(saved);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        #endregion
    }
}
