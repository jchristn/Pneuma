namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Exposes the embedding and completion model endpoints (native model runners) available to the dashboards.
    /// </summary>
    public class IngestionEndpointRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate ingestion endpoint routes.</summary>
        /// <param name="db">Database driver (native model-runner store).</param>
        /// <param name="authz">Authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public IngestionEndpointRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            _Db = db;
            _Authz = authz;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
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
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (!runner.Active) continue;
                if (runner.Capabilities.Contains(ModelCapabilityEnum.Embedding)) response.Embedding.Add(ToDto(runner, "Embedding"));
                if (runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) response.Completion.Add(ToDto(runner, "Completion"));
            }

            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private static ModelEndpointDto ToDto(ModelRunner runner, string type)
        {
            return new ModelEndpointDto
            {
                Id = runner.Id,
                Type = type,
                Provider = runner.Provider,
                Name = runner.Name,
                Model = String.Equals(type, "Embedding", StringComparison.OrdinalIgnoreCase) ? (runner.DefaultEmbeddingModel ?? runner.DefaultModel) : (runner.DefaultModel ?? runner.DefaultEmbeddingModel),
                Endpoint = runner.BaseUrl,
                ApiFormat = runner.ApiType,
                ApiKey = null,
                Deployment = runner.Deployment,
                ApiVersion = runner.ApiVersion,
                Region = runner.Region,
                Project = runner.Project,
                AccessKeyId = runner.AccessKeyId,
                Active = runner.Active,
                ContextSize = runner.ContextSize,
                CreatedUtc = runner.CreatedUtc
            };
        }

        #endregion
    }
}
