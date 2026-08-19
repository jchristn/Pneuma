namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Prompt management routes. Protected prompts cannot be deleted.
    /// </summary>
    public class PromptRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate prompt routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public PromptRoutes(DatabaseDriverBase db, AuthorizationService authz)
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
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/prompts", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List prompts", "Prompts"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/prompts", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/prompts/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/prompts/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/prompts/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a prompt", "Prompts"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Prompt, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            List<Prompt> prompts = await _Db.Prompts.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<Prompt> result = EnumerationHelper.Paginate(prompts, RouteHelper.ReadEnumerationQuery(ctx), p => p.CreatedUtc, p => p.Key);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            Prompt? prompt = RouteHelper.ReadBody<Prompt>(ctx);
            if (prompt == null || String.IsNullOrWhiteSpace(prompt.Key))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Prompt key is required.").ConfigureAwait(false);
                return;
            }
            prompt.TenantId = rc.TenantId;
            Prompt created = await _Db.Prompts.CreateAsync(prompt, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Prompt? prompt = await _Db.Prompts.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (prompt == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Prompt not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, prompt).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            Prompt? existing = await _Db.Prompts.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Prompt not found.").ConfigureAwait(false);
                return;
            }
            Prompt? update = RouteHelper.ReadBody<Prompt>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            existing.Name = String.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name;
            existing.Content = update.Content;
            existing.Version = update.Version;
            existing.Active = update.Active;
            Prompt saved = await _Db.Prompts.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            Prompt? existing = await _Db.Prompts.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing != null && existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Prompt is protected.").ConfigureAwait(false);
                return;
            }
            bool deleted = await _Db.Prompts.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Prompt not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
