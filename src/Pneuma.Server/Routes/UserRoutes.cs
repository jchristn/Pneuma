namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
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
    /// User management routes, tenant-scoped from the request context (admin may widen with ?tenantId=).
    /// </summary>
    public class UserRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate user routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public UserRoutes(DatabaseDriverBase db, AuthorizationService authz)
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

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/users", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List users", "Users"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/users", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/users/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/users/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a user", "Users"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/users/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a user", "Users"));
        }

        #endregion

        #region Private-Methods

        private string ScopeTenant(RequestContext rc, HttpContextBase ctx)
        {
            if (rc.IsAdmin)
            {
                string? q = ctx.Request.Query.Elements?["tenantId"];
                if (!String.IsNullOrEmpty(q)) return q;
            }
            return rc.TenantId ?? String.Empty;
        }

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.User, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = ScopeTenant(rc, ctx);
            List<User> users = await _Db.Users.EnumerateAsync(tenantId, ctx.Token).ConfigureAwait(false);
            foreach (User u in users) u.PasswordSha256 = String.Empty;
            EnumerationResult<User> result = EnumerationHelper.Paginate(users, RouteHelper.ReadEnumerationQuery(ctx), u => u.CreatedUtc, u => u.Email);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;

            CreateUserRequest? request = RouteHelper.ReadBody<CreateUserRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Email) || String.IsNullOrWhiteSpace(request.Password))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Email and password are required.").ConfigureAwait(false);
                return;
            }

            string tenantId = !String.IsNullOrEmpty(request.TenantId) && rc.IsAdmin ? request.TenantId! : (rc.TenantId ?? String.Empty);
            if (String.IsNullOrEmpty(tenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }

            User user = new User
            {
                TenantId = tenantId,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = request.Email,
                PasswordSha256 = PasswordHasher.Hash(request.Password),
                IsAdmin = rc.IsAdmin && request.IsAdmin,
                IsTenantAdmin = request.IsTenantAdmin
            };
            User created = await _Db.Users.CreateAsync(user, ctx.Token).ConfigureAwait(false);
            created.PasswordSha256 = String.Empty;
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            string tenantId = ScopeTenant(rc, ctx);
            User? user = await _Db.Users.ReadAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (user == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }
            user.PasswordSha256 = String.Empty;
            await RouteHelper.SendJsonAsync(ctx, 200, user).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            string tenantId = ScopeTenant(rc, ctx);
            User? existing = await _Db.Users.ReadAsync(tenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }
            CreateUserRequest? update = RouteHelper.ReadBody<CreateUserRequest>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            if (!String.IsNullOrWhiteSpace(update.FirstName)) existing.FirstName = update.FirstName;
            if (!String.IsNullOrWhiteSpace(update.LastName)) existing.LastName = update.LastName;
            if (!String.IsNullOrWhiteSpace(update.Password)) existing.PasswordSha256 = PasswordHasher.Hash(update.Password);
            existing.IsTenantAdmin = update.IsTenantAdmin;
            if (rc.IsAdmin) existing.IsAdmin = update.IsAdmin;
            User saved = await _Db.Users.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            saved.PasswordSha256 = String.Empty;
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            string tenantId = ScopeTenant(rc, ctx);
            string id = RouteHelper.Param(ctx, "id");

            // Cascade: remove credentials owned by the user.
            List<Credential> credentials = await _Db.Credentials.EnumerateByUserAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            foreach (Credential credential in credentials)
            {
                await _Db.Credentials.DeleteAsync(tenantId, credential.Id, ctx.Token).ConfigureAwait(false);
            }

            bool deleted = await _Db.Users.DeleteAsync(tenantId, id, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "User not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
