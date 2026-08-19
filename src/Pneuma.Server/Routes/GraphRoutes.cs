namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Knowledge-graph read routes for the user experience: node contents, adjacency, and relationships.
    /// </summary>
    public class GraphRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly IGraphRepositoryFactory _GraphFactory;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate graph routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        public GraphRoutes(DatabaseDriverBase db, AuthorizationService authz, IGraphRepositoryFactory graphFactory)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            _Db = db;
            _Authz = authz;
            _GraphFactory = graphFactory;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/graph/nodes/{id}", ReadNodeAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a graph node", "Graph"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/graph/nodes/{id}/neighbors", NeighborsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get adjacent nodes", "Graph"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/graph/nodes/{id}/edges", EdgesAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get node relationships (edges)", "Graph"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ReadNodeAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            GraphNode? node = await (await _GraphFactory.ForTenantAsync(rc.TenantId ?? String.Empty, ctx.Token).ConfigureAwait(false)).ReadNodeAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (node == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Node not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, node).ConfigureAwait(false);
        }

        private async Task NeighborsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            List<GraphNode> neighbors = await (await _GraphFactory.ForTenantAsync(rc.TenantId ?? String.Empty, ctx.Token).ConfigureAwait(false)).GetNeighborsAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, neighbors).ConfigureAwait(false);
        }

        private async Task EdgesAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc).ConfigureAwait(false)) return;
            List<GraphEdge> edges = await (await _GraphFactory.ForTenantAsync(rc.TenantId ?? String.Empty, ctx.Token).ConfigureAwait(false)).GetEdgesAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, edges).ConfigureAwait(false);
        }

        #endregion
    }
}
