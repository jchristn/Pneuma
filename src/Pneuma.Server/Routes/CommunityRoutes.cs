namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// GraphRAG community routes for a subject: (re)build the community summaries used by the global/thematic
    /// query mode, and list the current summaries.
    /// </summary>
    public class CommunityRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly CommunityService _Communities;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate community routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="communities">Community service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public CommunityRoutes(DatabaseDriverBase db, AuthorizationService authz, CommunityService communities)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Communities = communities ?? throw new ArgumentNullException(nameof(communities));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{subjectId}/communities/build", BuildAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Detect communities and (re)build the subject's community summaries", "Communities"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{subjectId}/communities", ListAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List the subject's community summaries", "Communities"));
        }

        #endregion

        #region Private-Methods

        private async Task<Subject?> ResolveSubjectAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, op, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return null;
            }
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return null;
            }
            Subject? subject = await _Db.Subjects.ReadAsync(rc.TenantId, RouteHelper.Param(ctx, "subjectId"), ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return null;
            }
            return subject;
        }

        private async Task BuildAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            Subject? subject = await ResolveSubjectAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false);
            if (subject == null) return;

            int created = await _Communities.BuildAsync(rc.TenantId!, subject.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { subjectId = subject.Id, summariesCreated = created }).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            Subject? subject = await ResolveSubjectAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false);
            if (subject == null) return;

            List<GraphNode> summaries = await _Communities.ListSummariesAsync(rc.TenantId!, subject.Id, ctx.Token).ConfigureAwait(false);
            List<object> objects = new List<object>();
            foreach (GraphNode node in summaries)
            {
                node.Tags.TryGetValue(Ontology.TagCommunityId, out string? communityId);
                int memberCount = 0;
                if (node.Tags.TryGetValue(Ontology.TagMemberCount, out string? memberRaw)) Int32.TryParse(memberRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out memberCount);
                objects.Add(new
                {
                    communityId = communityId ?? String.Empty,
                    label = node.Name,
                    summary = node.Content,
                    memberCount
                });
            }
            await RouteHelper.SendJsonAsync(ctx, 200, new { subjectId = subject.Id, communities = objects }).ConfigureAwait(false);
        }

        #endregion
    }
}
