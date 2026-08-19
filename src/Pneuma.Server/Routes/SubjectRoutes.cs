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
    /// Subject archive management routes, scoped to the caller's tenant.
    /// </summary>
    public class SubjectRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly CascadeDeletionService _Cascade;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate subject routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="cascade">Cascade deletion service, used to remove a subject's subordinate objects.</param>
        public SubjectRoutes(DatabaseDriverBase db, AuthorizationService authz, CascadeDeletionService cascade)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (cascade == null) throw new ArgumentNullException(nameof(cascade));
            _Db = db;
            _Authz = authz;
            _Cascade = cascade;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/subjects", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List subjects", "Subjects"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/subjects", CreateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a subject", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{id}", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read a subject", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/subjects/{id}", UpdateAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Update a subject", "Subjects"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/subjects/{id}", DeleteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a subject", "Subjects"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, op, null, ctx.Token).ConfigureAwait(false)) return true;
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
            List<Subject> subjects = await _Db.Subjects.EnumerateAsync(rc.TenantId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<Subject> result = EnumerationHelper.Paginate(subjects, RouteHelper.ReadEnumerationQuery(ctx), c => c.CreatedUtc, c => c.DisplayName);
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
            Subject? subject = RouteHelper.ReadBody<Subject>(ctx);
            if (subject == null || String.IsNullOrWhiteSpace(subject.DisplayName))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Subject display name is required.").ConfigureAwait(false);
                return;
            }
            subject.TenantId = rc.TenantId;
            if (String.IsNullOrWhiteSpace(subject.GraphRootNodeId)) subject.GraphRootNodeId = SlugHelper.Slugify(subject.DisplayName);
            Subject created = await _Db.Subjects.CreateAsync(subject, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            Subject? subject = await _Db.Subjects.ReadAsync(rc.TenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }
            await RouteHelper.SendJsonAsync(ctx, 200, subject).ConfigureAwait(false);
        }

        private async Task UpdateAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            Subject? existing = await _Db.Subjects.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (existing == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }
            Subject? update = RouteHelper.ReadBody<Subject>(ctx);
            if (update == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Body required.").ConfigureAwait(false);
                return;
            }
            existing.DisplayName = String.IsNullOrWhiteSpace(update.DisplayName) ? existing.DisplayName : update.DisplayName;
            existing.Type = update.Type;
            existing.Description = update.Description;
            existing.Active = update.Active;
            Subject saved = await _Db.Subjects.UpdateAsync(existing, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, saved).ConfigureAwait(false);
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
            string subjectId = RouteHelper.Param(ctx, "id");
            Subject? subject = await _Db.Subjects.ReadAsync(rc.TenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }

            // Cascade: delete every link (and its jobs, logs, artifacts, graph nodes, and index documents)
            // and the subject's knowledge-graph subgraph before removing the subject row.
            bool deleted = await _Cascade.DeleteSubjectCascadeAsync(rc.TenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send().ConfigureAwait(false);
        }

        #endregion
    }
}
