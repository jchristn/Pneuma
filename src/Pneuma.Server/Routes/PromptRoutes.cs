namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Prompt management routes. System prompts (no tenant) are the defaults every tenant starts from; a tenant can keep
    /// its own copy of any of them. Editing a system prompt as a tenant user creates or updates the tenant's copy, so one
    /// tenant never changes another's behavior; only a system administrator edits the system default itself. Deleting a
    /// tenant copy resets the tenant to the system default. Protected (system) prompts cannot be deleted.
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
                openApiMetadata: OpenApiRouteMetadata.Create("List the prompts in effect for the tenant: its own copy of each key, else the system default (?scope=system lists system defaults only; ?scope=tenant the tenant's copies only)", "Prompts"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/prompts", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a tenant prompt", "Prompts").WithRequestBody(OpenApiBodies.Json<Prompt>("Create a prompt")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/prompts/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a prompt", "Prompts"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/prompts/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a prompt; editing a system prompt as a tenant user (or with ?scope=tenant) saves a tenant copy instead", "Prompts").WithRequestBody(OpenApiBodies.Json<Prompt>("Update a prompt")));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/prompts/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a tenant prompt (deleting a tenant copy resets the tenant to the system default)", "Prompts"));
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
            List<Prompt> all = await _Db.Prompts.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            string? scope = RouteHelper.Query(ctx, "scope");
            List<Prompt> prompts;
            if (String.Equals(scope, "system", StringComparison.OrdinalIgnoreCase)) prompts = all.Where(p => p.IsSystemDefault).ToList();
            else if (String.Equals(scope, "tenant", StringComparison.OrdinalIgnoreCase)) prompts = all.Where(p => !p.IsSystemDefault).ToList();
            else prompts = Effective(all);
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
            Prompt? clash = await _Db.Prompts.ReadByKeyAsync(rc.TenantId, prompt.Key, ctx.Token).ConfigureAwait(false);
            if (clash != null && !clash.IsSystemDefault)
            {
                await RouteHelper.SendErrorAsync(ctx, 409, "Conflict", "The tenant already has a prompt with key '" + prompt.Key + "'; update it instead.").ConfigureAwait(false);
                return;
            }
            prompt.Id = IdGenerator.GeneratePromptId();
            prompt.TenantId = rc.TenantId;
            prompt.IsProtected = false;
            Prompt created = await _Db.Prompts.CreateAsync(prompt, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Prompt? prompt = await ReadVisibleAsync(ctx, rc).ConfigureAwait(false);
            if (prompt == null) return;
            await RouteHelper.SendJsonAsync(ctx, 200, prompt).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            Prompt? existing = await ReadVisibleAsync(ctx, rc).ConfigureAwait(false);
            if (existing == null) return;
            Prompt? update = RouteHelper.ReadBody<Prompt>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }

            bool forceTenantCopy = String.Equals(RouteHelper.Query(ctx, "scope"), "tenant", StringComparison.OrdinalIgnoreCase);
            if (existing.IsSystemDefault && (!rc.IsAdmin || forceTenantCopy))
            {
                if (String.IsNullOrEmpty(rc.TenantId))
                {
                    await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                    return;
                }
                Prompt saved = await SaveTenantCopyAsync(rc.TenantId!, existing, update, ctx).ConfigureAwait(false);
                await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
                return;
            }

            existing.Name = String.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name;
            existing.Content = update.Content;
            existing.Version = Math.Max(update.Version, existing.Version + 1);
            existing.Active = update.Active;
            Prompt result = await _Db.Prompts.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            Prompt? existing = await ReadVisibleAsync(ctx, rc).ConfigureAwait(false);
            if (existing == null) return;
            if (existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Prompt is protected.").ConfigureAwait(false);
                return;
            }
            await _Db.Prompts.DeleteAsync(existing.Id, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task<Prompt> SaveTenantCopyAsync(string tenantId, Prompt system, Prompt update, HttpContextBase ctx)
        {
            Prompt? copy = await _Db.Prompts.ReadByKeyAsync(tenantId, system.Key, ctx.Token).ConfigureAwait(false);
            if (copy != null && !copy.IsSystemDefault)
            {
                copy.Content = update.Content;
                copy.Active = update.Active;
                copy.Version = copy.Version + 1;
                return await _Db.Prompts.UpdateAsync(copy, ctx.Token).ConfigureAwait(false);
            }
            Prompt created = new Prompt
            {
                TenantId = tenantId,
                Key = system.Key,
                Name = system.Name,
                Content = update.Content,
                Version = 1,
                Active = update.Active,
                IsProtected = false
            };
            return await _Db.Prompts.CreateAsync(created, ctx.Token).ConfigureAwait(false);
        }

        // A system prompt is visible to every tenant; a tenant prompt only to its own tenant.
        private async Task<Prompt?> ReadVisibleAsync(HttpContextBase ctx, RequestContext rc)
        {
            Prompt? prompt = await _Db.Prompts.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (prompt == null || (!prompt.IsSystemDefault && !String.Equals(prompt.TenantId, rc.TenantId, StringComparison.Ordinal)))
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Prompt not found.").ConfigureAwait(false);
                return null;
            }
            return prompt;
        }

        private static List<Prompt> Effective(List<Prompt> all)
        {
            Dictionary<string, Prompt> byKey = new Dictionary<string, Prompt>(StringComparer.Ordinal);
            foreach (Prompt prompt in all)
            {
                Prompt? current;
                if (!byKey.TryGetValue(prompt.Key, out current) || (current.IsSystemDefault && !prompt.IsSystemDefault)) byKey[prompt.Key] = prompt;
            }
            return all.Where(p => byKey.TryGetValue(p.Key, out Prompt? chosen) && ReferenceEquals(chosen, p)).ToList();
        }

        #endregion
    }
}
