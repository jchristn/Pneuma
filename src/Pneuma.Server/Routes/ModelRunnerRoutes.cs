namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Model runner routes. Model endpoints are stored natively in Pneuma (the <c>modelrunners</c> table);
    /// Pneuma addresses each provider directly through PolyPrompt. Secrets are encrypted at rest and never
    /// returned. Route paths and the "Model Runners" surface name are preserved for dashboard/SDK compatibility.
    /// </summary>
    public class ModelRunnerRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly ModelHealthMonitor _Health;
        private readonly ModelRunnerValidationService _Validation;
        private readonly Aes256Cipher _Cipher;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate model runner routes.</summary>
        /// <param name="db">Database driver (native model-runner store).</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="health">Model health monitor providing per-base-URL health status.</param>
        /// <param name="validation">Model runner validation service (active end-to-end endpoint checks).</param>
        /// <param name="cipher">Cipher used to encrypt endpoint secrets at rest.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ModelRunnerRoutes(DatabaseDriverBase db, AuthorizationService authz, ModelHealthMonitor health, ModelRunnerValidationService validation, Aes256Cipher cipher)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (health == null) throw new ArgumentNullException(nameof(health));
            if (validation == null) throw new ArgumentNullException(nameof(validation));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            _Db = db;
            _Authz = authz;
            _Health = health;
            _Validation = validation;
            _Cipher = cipher;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/model-runners", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List model endpoints", "ModelRunners"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/model-runners", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/model-runners/health", HealthListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Health of all model endpoints (deduplicated by base URL)", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/model-runners/{id}/health", HealthByIdAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Health of a single model endpoint", "ModelRunners"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/model-runners/{id}/validate", ValidateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Actively validate a model endpoint end to end (completion + tool calling, or embedding)", "ModelRunners"));
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

            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            List<ModelEndpointDto> all = new List<ModelEndpointDto>();
            foreach (ModelRunner runner in runners) all.Add(ToDto(runner));

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

            ModelRunner runner = new ModelRunner
            {
                TenantId = rc.TenantId
            };
            ApplyRequest(runner, request, type);
            ModelRunner created = await _Db.ModelRunners.CreateAsync(runner, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, ToDto(created)).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            ModelRunner? runner = await _Db.ModelRunners.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (runner == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, ToDto(runner)).ConfigureAwait(false);
        }

        private async Task HealthListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            List<ModelEndpointHealthDto> health = new List<ModelEndpointHealthDto>();
            foreach (ModelRunner runner in runners) health.Add(_Health.BuildStatus(runner.Id, runner.Name, TypeOf(runner), runner.BaseUrl));
            await RouteHelper.SendJsonAsync(ctx, 200, health).ConfigureAwait(false);
        }

        private async Task HealthByIdAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            ModelRunner? runner = await _Db.ModelRunners.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (runner == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            ModelEndpointHealthDto status = _Health.BuildStatus(runner.Id, runner.Name, TypeOf(runner), runner.BaseUrl);
            await RouteHelper.SendJsonAsync(ctx, 200, status).ConfigureAwait(false);
        }

        private async Task ValidateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            string? type = ctx.Request.Query.Elements?["type"];
            ModelEndpointValidationDto? result = await _Validation.ValidateAsync(id, type, ctx.Token).ConfigureAwait(false);
            if (result == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Admin).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            ModelRunner? existing = await _Db.ModelRunners.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }

            CreateModelEndpointRequest? request = RouteHelper.ReadBody<CreateModelEndpointRequest>(ctx);
            if (request == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A request body is required.").ConfigureAwait(false);
                return;
            }

            string type = NormalizeType(request.Type) ?? TypeOf(existing);
            ApplyRequest(existing, request, type);
            ModelRunner updated = await _Db.ModelRunners.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, ToDto(updated)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Admin).ConfigureAwait(false)) return;

            string id = RouteHelper.Param(ctx, "id");
            ModelRunner? existing = await _Db.ModelRunners.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Model endpoint not found.").ConfigureAwait(false);
                return;
            }
            if (existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Model endpoint is protected.").ConfigureAwait(false);
                return;
            }
            await _Db.ModelRunners.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        // Apply create/update request fields onto a runner. Secrets are re-encrypted only when supplied, so an
        // update that omits the key preserves the stored credential.
        private void ApplyRequest(ModelRunner runner, CreateModelEndpointRequest request, string type)
        {
            bool embedding = String.Equals(type, "embedding", StringComparison.OrdinalIgnoreCase);
            runner.Name = String.IsNullOrWhiteSpace(request.Name) ? (String.IsNullOrWhiteSpace(request.Model) ? runner.Name : request.Model!) : request.Name!;
            runner.Provider = request.Provider ?? MapApiFormat(request.ApiFormat);
            if (!String.IsNullOrWhiteSpace(request.Endpoint)) runner.BaseUrl = request.Endpoint!;
            runner.ApiType = request.ApiFormat;
            runner.Capabilities = new List<ModelCapabilityEnum> { embedding ? ModelCapabilityEnum.Embedding : ModelCapabilityEnum.Completion };
            runner.Usage = embedding ? ModelRunnerUsageEnum.Ingestion : ModelRunnerUsageEnum.Both;
            runner.DefaultModel = embedding ? null : request.Model;
            runner.DefaultEmbeddingModel = embedding ? request.Model : null;
            runner.Deployment = request.Deployment;
            runner.ApiVersion = request.ApiVersion;
            runner.Region = request.Region;
            runner.Project = request.Project;
            runner.AccessKeyId = request.AccessKeyId;
            runner.ContextSize = Math.Max(0, request.ContextSize);
            runner.Active = request.Active;
            if (!String.IsNullOrEmpty(request.ApiKey)) runner.AuthMaterialEncrypted = _Cipher.Encrypt(request.ApiKey);
            if (!String.IsNullOrEmpty(request.SessionToken)) runner.SessionTokenEncrypted = _Cipher.Encrypt(request.SessionToken);
        }

        private static string? NormalizeType(string? type)
        {
            if (String.IsNullOrWhiteSpace(type)) return null;
            string lowered = type.Trim().ToLowerInvariant();
            if (lowered == "embedding") return "embedding";
            if (lowered == "completion") return "completion";
            return null;
        }

        private static string TypeOf(ModelRunner runner)
        {
            if (runner.Capabilities.Contains(ModelCapabilityEnum.Embedding) && !runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) return "Embedding";
            return "Completion";
        }

        private static ModelRunnerProviderEnum MapApiFormat(string? apiFormat)
        {
            if (String.IsNullOrWhiteSpace(apiFormat)) return ModelRunnerProviderEnum.Ollama;
            string value = apiFormat.Trim().ToLowerInvariant();
            switch (value)
            {
                case "openai": return ModelRunnerProviderEnum.OpenAI;
                case "openaicompatible":
                case "vllm":
                case "lmstudio": return ModelRunnerProviderEnum.OpenAICompatible;
                case "gemini": return ModelRunnerProviderEnum.Gemini;
                case "ollama": return ModelRunnerProviderEnum.Ollama;
                case "azure":
                case "azureopenai": return ModelRunnerProviderEnum.AzureOpenAI;
                case "anthropic": return ModelRunnerProviderEnum.Anthropic;
                case "bedrock": return ModelRunnerProviderEnum.Bedrock;
                case "voyage":
                case "voyageai": return ModelRunnerProviderEnum.VoyageAI;
                case "vertex":
                case "vertexai": return ModelRunnerProviderEnum.VertexAI;
                default: return ModelRunnerProviderEnum.Ollama;
            }
        }

        private static ModelEndpointDto ToDto(ModelRunner runner)
        {
            return new ModelEndpointDto
            {
                Id = runner.Id,
                Type = TypeOf(runner),
                Provider = runner.Provider,
                Name = runner.Name,
                Model = runner.DefaultModel ?? runner.DefaultEmbeddingModel,
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
