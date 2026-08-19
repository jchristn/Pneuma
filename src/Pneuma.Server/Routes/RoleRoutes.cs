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
    /// Role management routes. Built-in (protected) roles cannot be updated or deleted.
    /// </summary>
    public class RoleRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate role routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public RoleRoutes(DatabaseDriverBase db, AuthorizationService authz)
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

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/roles", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List roles", "Authorization"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/roles", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a role", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/roles/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a role", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/roles/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a role", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/roles/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a role", "Authorization"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Role, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            List<UserRole> roles = await _Db.Roles.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<UserRole> result = EnumerationHelper.Paginate(roles, RouteHelper.ReadEnumerationQuery(ctx), r => r.CreatedUtc, r => r.Name);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            UserRole? role = RouteHelper.ReadBody<UserRole>(ctx);
            if (role == null || String.IsNullOrWhiteSpace(role.Name))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Role name is required.").ConfigureAwait(false);
                return;
            }
            if (!role.IsBuiltIn) role.TenantId = rc.TenantId;
            UserRole created = await _Db.Roles.CreateAsync(role, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            UserRole? role = await _Db.Roles.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (role == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Role not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, role).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            UserRole? existing = await _Db.Roles.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Role not found.").ConfigureAwait(false);
                return;
            }
            if (existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Role is protected.").ConfigureAwait(false);
                return;
            }
            UserRole? update = RouteHelper.ReadBody<UserRole>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            existing.Name = String.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name;
            existing.Active = update.Active;
            UserRole saved = await _Db.Roles.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            UserRole? existing = await _Db.Roles.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing != null && existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Role is protected.").ConfigureAwait(false);
                return;
            }
            bool deleted = await _Db.Roles.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Role not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
