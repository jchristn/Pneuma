namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Vector-collection administration. Collections live in the retrieval store (RecallDB); these routes
    /// proxy list/read/create/delete so operators can define collections in the dashboard and ingestion can
    /// require one. Pneuma keeps no local collection state.
    /// </summary>
    public class CollectionRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly ICollectionStore _Collections;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate collection routes.</summary>
        /// <param name="authz">Authorization service.</param>
        /// <param name="collections">Collection store (RecallDB).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public CollectionRoutes(AuthorizationService authz, ICollectionStore collections)
        {
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            _Authz = authz;
            _Collections = collections;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/collections", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List vector collections", "Collections"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/v1.0/collections", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a vector collection", "Collections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/collections/{collectionId}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a vector collection", "Collections"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/collections/{collectionId}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a vector collection and all of its documents", "Collections"));
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

            List<RecallCollection> collections = await _Collections.ListCollectionsAsync(rc.TenantId ?? String.Empty, ctx.Token).ConfigureAwait(false);
            EnumerationResult<RecallCollection> result = new EnumerationResult<RecallCollection>
            {
                Success = true,
                MaxResults = collections.Count,
                Skip = 0,
                TotalRecords = collections.Count,
                RecordsRemaining = 0,
                EndOfResults = true,
                Objects = collections
            };
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            string collectionId = RouteHelper.Param(ctx, "collectionId");
            RecallCollection? collection = await _Collections.ReadCollectionAsync(rc.TenantId ?? String.Empty, collectionId, ctx.Token).ConfigureAwait(false);
            if (collection == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Collection not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, collection).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;

            CreateCollectionRequest? request = RouteHelper.ReadBody<CreateCollectionRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Name))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A collection name is required.").ConfigureAwait(false);
                return;
            }
            if (request.Dimensionality < 1)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Dimensionality must be a positive integer.").ConfigureAwait(false);
                return;
            }

            // RecallDB is the authority for collections and assigns the id; Pneuma relays name/description/
            // dimensionality without minting or storing an id of its own.
            RecallCollection collection = new RecallCollection
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                Dimensionality = request.Dimensionality,
                Active = true
            };
            RecallCollection created = await _Collections.CreateCollectionAsync(rc.TenantId ?? String.Empty, collection, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Write).ConfigureAwait(false)) return;

            string collectionId = RouteHelper.Param(ctx, "collectionId");
            bool deleted = await _Collections.DeleteCollectionAsync(rc.TenantId ?? String.Empty, collectionId, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Collection not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
