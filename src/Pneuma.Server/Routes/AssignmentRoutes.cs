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
    /// User role assignment management routes (the authoritative RBAC assignment table).
    /// </summary>
    public class AssignmentRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate assignment routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        public AssignmentRoutes(DatabaseDriverBase db, AuthorizationService authz)
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

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/assignments", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List assignments for a user", "Authorization"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/assignments", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create an assignment", "Authorization"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/assignments/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete an assignment", "Authorization"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Assignment, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string? userId = ctx.Request.Query.Elements?["userId"];
            if (String.IsNullOrWhiteSpace(userId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Query parameter 'userId' is required.").ConfigureAwait(false);
                return;
            }
            List<UserRoleAssignment> assignments = await _Db.UserRoleAssignments.EnumerateByUserAsync(rc.TenantId, userId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<UserRoleAssignment> result = EnumerationHelper.Paginate(assignments, RouteHelper.ReadEnumerationQuery(ctx), a => a.CreatedUtc, a => a.RoleName);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            UserRoleAssignment? assignment = RouteHelper.ReadBody<UserRoleAssignment>(ctx);
            if (assignment == null || String.IsNullOrWhiteSpace(assignment.UserId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Assignment user is required.").ConfigureAwait(false);
                return;
            }
            assignment.TenantId = rc.TenantId;
            UserRoleAssignment created = await _Db.UserRoleAssignments.CreateAsync(assignment, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            bool deleted = await _Db.UserRoleAssignments.DeleteAsync(rc.TenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Assignment not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
