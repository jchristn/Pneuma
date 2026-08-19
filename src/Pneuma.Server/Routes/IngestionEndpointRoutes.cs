namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Exposes the embedding and completion model endpoints available in Partio to the dashboards.
    /// </summary>
    public class IngestionEndpointRoutes
    {
        #region Private-Members

        private readonly IPartioClient _Partio;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate ingestion endpoint routes.</summary>
        /// <param name="partio">Partio client.</param>
        /// <param name="authz">Authorization service.</param>
        public IngestionEndpointRoutes(IPartioClient partio, AuthorizationService authz)
        {
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            _Partio = partio;
            _Authz = authz;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/ingestion/endpoints", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List available embedding and completion model endpoints", "Ingestion"));
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

            IngestionEndpointsResponse response = new IngestionEndpointsResponse();
            try
            {
                response.Embedding = await _Partio.ListEmbeddingEndpointsAsync(ctx.Token).ConfigureAwait(false);
                response.Completion = await _Partio.ListCompletionEndpointsAsync(ctx.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Partio may be unavailable; return empty lists so the dashboard still loads.
                response.Embedding = new List<PartioEndpoint>();
                response.Completion = new List<PartioEndpoint>();
            }

            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        #endregion
    }
}
