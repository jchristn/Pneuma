namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Retrieval-facet discovery routes: return the distinct labels and tag key/values an operator has applied
    /// to a subject's content, so the Ask "Scope" filter can suggest real values instead of free-typed guesses.
    /// The values are a bounded aggregate over the subject's links, not an enumeration of link rows.
    /// </summary>
    public class FacetRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly FacetDiscoveryService _Facets;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate facet discovery routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public FacetRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Facets = new FacetDiscoveryService(db);
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/retrieval/labels", LabelsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's distinct retrieval labels", "Retrieval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}/retrieval/tags", TagsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List a subject's distinct retrieval tag keys and values", "Retrieval"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task LabelsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string subjectId = RouteHelper.Param(ctx, "id");
            List<string> labels = await _Facets.DistinctLabelsAsync(rc.TenantId, subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { subjectId, labels }).ConfigureAwait(false);
        }

        private async Task TagsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string subjectId = RouteHelper.Param(ctx, "id");
            Dictionary<string, List<string>> tags = await _Facets.DistinctTagsAsync(rc.TenantId, subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { subjectId, tags }).ConfigureAwait(false);
        }

        #endregion
    }
}
