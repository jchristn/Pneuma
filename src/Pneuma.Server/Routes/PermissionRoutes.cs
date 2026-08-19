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
    /// Permission management routes. Built-in (protected) permissions cannot be updated or deleted.
    /// </summary>
    public class PermissionRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate permission routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public PermissionRoutes(DatabaseDriverBase db, AuthorizationService authz)
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

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/permissions", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List permissions", "Authorization"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/permissions", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a permission", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/permissions/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a permission", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/permissions/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a permission", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/permissions/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a permission", "Authorization"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Permission, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            List<Permission> permissions = await _Db.Permissions.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<Permission> result = EnumerationHelper.Paginate(permissions, RouteHelper.ReadEnumerationQuery(ctx), p => p.CreatedUtc, p => p.Name);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            Permission? permission = RouteHelper.ReadBody<Permission>(ctx);
            if (permission == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            permission.TenantId = rc.TenantId;
            Permission created = await _Db.Permissions.CreateAsync(permission, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            Permission? permission = await _Db.Permissions.ReadAsync(RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (permission == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Permission not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, permission).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            Permission? existing = await _Db.Permissions.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Permission not found.").ConfigureAwait(false);
                return;
            }
            if (existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Permission is protected.").ConfigureAwait(false);
                return;
            }
            Permission? update = RouteHelper.ReadBody<Permission>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            existing.Name = String.IsNullOrWhiteSpace(update.Name) ? existing.Name : update.Name;
            existing.ResourceTypes = update.ResourceTypes;
            existing.OperationTypes = update.OperationTypes;
            existing.PermissionType = update.PermissionType;
            existing.Active = update.Active;
            Permission saved = await _Db.Permissions.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string id = RouteHelper.Param(ctx, "id");
            Permission? existing = await _Db.Permissions.ReadAsync(id, ctx.Token).ConfigureAwait(false);
            if (existing != null && existing.IsProtected)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "Protected", "Permission is protected.").ConfigureAwait(false);
                return;
            }
            bool deleted = await _Db.Permissions.DeleteAsync(id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Permission not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
