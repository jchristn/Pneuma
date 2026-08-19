namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Model runner routes. A pass-through proxy to Partio's embedding and completion endpoints:
    /// Pneuma stores no local model state, it simply manages Partio endpoints on the operator's behalf.
    /// </summary>
    public class ModelRunnerRoutes
    {
        #region Private-Members

        private readonly IPartioClient _Partio;
        private readonly AuthorizationService _Authz;
        private readonly ModelHealthMonitor _Health;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate model runner (Partio endpoint proxy) routes.</summary>
        /// <param name="partio">Partio client.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="health">Model health monitor providing per-base-URL health status.</param>
        public ModelRunnerRoutes(IPartioClient partio, AuthorizationService authz, ModelHealthMonitor health)
        {
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (health == null) throw new ArgumentNullException(nameof(health));
            _Partio = partio;
            _Authz = authz;
            _Health = health;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/model-runners", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List model endpoints (Partio)", "ModelRunners"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/model-runners", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/model-runners/health", HealthListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Health of all model endpoints (deduplicated by base URL)", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/model-runners/{id}/health", HealthByIdAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Health of a single model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/model-runners/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/model-runners/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/model-runners/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a model endpoint", "ModelRunners"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.ModelRunner, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            List<ModelEndpointDto> all = new List<ModelEndpointDto>();
            List<PartioEndpoint> embedding = await _Partio.ListEmbeddingEndpointsAsync(ctx.Token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in embedding) all.Add(ToDto(endpoint, "Embedding"));
            List<PartioEndpoint> completion = await _Partio.ListCompletionEndpointsAsync(ctx.Token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in completion) all.Add(ToDto(endpoint, "Completion"));

            EnumerationResult<ModelEndpointDto> result = EnumerationHelper.Paginate(all, RouteHelper.ReadEnumerationQuery(ctx), e => e.CreatedUtc, e => e.Name);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Admin).ConfigureAwait(false)) return;

            CreateModelEndpointRequest? request = RouteHelper.ReadBody<CreateModelEndpointRequest>(ctx);
            string? type = NormalizeType(request?.Type);
            if (request == null || type == null || String.IsNullOrWhiteSpace(request.Model) || String.IsNullOrWhiteSpace(request.Endpoint))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "type (Embedding|Completion), model, and endpoint are required.").ConfigureAwait(false);
                return;
            }

            PartioEndpoint created = await _Partio.CreateEndpointAsync(type, ToEndpoint(request), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, ToDto(created, DisplayType(type))).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            PartioEndpoint? endpoint = await _Partio.ReadEndpointAsync("embedding", id, ctx.Token).ConfigureAwait(false);
            string display = "Embedding";
            if (endpoint == null)
            {
                endpoint = await _Partio.ReadEndpointAsync("completion", id, ctx.Token).ConfigureAwait(false);
                display = "Completion";
            }
            if (endpoint == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, ToDto(endpoint, display)).ConfigureAwait(false);
        }

        private async Task HealthListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            List<ModelEndpointHealthDto> health = new List<ModelEndpointHealthDto>();
            List<PartioEndpoint> embedding = await _Partio.ListEmbeddingEndpointsAsync(ctx.Token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in embedding) health.Add(_Health.BuildStatus(endpoint.Id, endpoint.Name, "Embedding", endpoint.Endpoint));
            List<PartioEndpoint> completion = await _Partio.ListCompletionEndpointsAsync(ctx.Token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in completion) health.Add(_Health.BuildStatus(endpoint.Id, endpoint.Name, "Completion", endpoint.Endpoint));

            await RouteHelper.SendJsonAsync(ctx, 200, health).ConfigureAwait(false);
        }

        private async Task HealthByIdAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            PartioEndpoint? endpoint = await _Partio.ReadEndpointAsync("embedding", id, ctx.Token).ConfigureAwait(false);
            string display = "Embedding";
            if (endpoint == null)
            {
                endpoint = await _Partio.ReadEndpointAsync("completion", id, ctx.Token).ConfigureAwait(false);
                display = "Completion";
            }
            if (endpoint == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }

            ModelEndpointHealthDto status = _Health.BuildStatus(endpoint.Id, endpoint.Name, display, endpoint.Endpoint);
            await RouteHelper.SendJsonAsync(ctx, 200, status).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Admin).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            CreateModelEndpointRequest? request = RouteHelper.ReadBody<CreateModelEndpointRequest>(ctx);
            string? type = NormalizeType(request?.Type) ?? await ResolveTypeAsync(id, ctx.Token).ConfigureAwait(false);
            if (request == null || type == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A body with a valid type (Embedding|Completion) is required.").ConfigureAwait(false);
                return;
            }

            PartioEndpoint updated = await _Partio.UpdateEndpointAsync(type, id, ToEndpoint(request), ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, ToDto(updated, DisplayType(type))).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Admin).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            string? type = await ResolveTypeAsync(id, ctx.Token).ConfigureAwait(false);
            if (type == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            await _Partio.DeleteEndpointAsync(type, id, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task<string?> ResolveTypeAsync(string id, System.Threading.CancellationToken token)
        {
            PartioEndpoint? embedding = await _Partio.ReadEndpointAsync("embedding", id, token).ConfigureAwait(false);
            if (embedding != null) return "embedding";
            PartioEndpoint? completion = await _Partio.ReadEndpointAsync("completion", id, token).ConfigureAwait(false);
            if (completion != null) return "completion";
            return null;
        }

        private static string? NormalizeType(string? type)
        {
            if (String.IsNullOrWhiteSpace(type)) return null;
            string lowered = type.Trim().ToLowerInvariant();
            if (lowered == "embedding") return "embedding";
            if (lowered == "completion") return "completion";
            return null;
        }

        private static string DisplayType(string type)
        {
            return type == "completion" ? "Completion" : "Embedding";
        }

        private static ModelEndpointDto ToDto(PartioEndpoint endpoint, string type)
        {
            return new ModelEndpointDto
            {
                Id = endpoint.Id,
                Type = type,
                Name = endpoint.Name,
                Model = endpoint.Model,
                Endpoint = endpoint.Endpoint,
                ApiFormat = endpoint.ApiFormat,
                Active = endpoint.Active
            };
        }

        private static PartioEndpoint ToEndpoint(CreateModelEndpointRequest request)
        {
            return new PartioEndpoint
            {
                Name = request.Name,
                Model = request.Model,
                Endpoint = request.Endpoint,
                ApiFormat = request.ApiFormat,
                ApiKey = request.ApiKey,
                Active = request.Active
            };
        }

        #endregion
    }
}
