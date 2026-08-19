namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// User-facing search: query Verbex and resolve hits to a representative set of graph nodes.
    /// </summary>
    public class SearchRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly IInvertedIndex _Verbex;
        private readonly IGraphRepository _Graph;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate search routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="verbex">Verbex client.</param>
        /// <param name="graph">LiteGraph client.</param>
        public SearchRoutes(DatabaseDriverBase db, AuthorizationService authz, IInvertedIndex verbex, IGraphRepository graph)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (verbex == null) throw new ArgumentNullException(nameof(verbex));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            _Db = db;
            _Authz = authz;
            _Verbex = verbex;
            _Graph = graph;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/search", SearchAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Search the corpus for representative nodes", "Search"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{subjectId}/search", SubjectSearchAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Search a subject's ingested documents (Verbex), paginated by score", "Search"));
        }

        #endregion

        #region Private-Methods

        private async Task SearchAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            string? query = ctx.Request.Query.Elements?["q"];
            if (String.IsNullOrWhiteSpace(query))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A query (q) is required.").ConfigureAwait(false);
                return;
            }

            int max = 20;
            string? maxText = ctx.Request.Query.Elements?["max"];
            if (!String.IsNullOrEmpty(maxText) && Int32.TryParse(maxText, out int parsed)) max = Math.Clamp(parsed, 1, 100);

            string indexId = await _Verbex.EnsureIndexAsync(ctx.Token).ConfigureAwait(false);
            List<VerbexHit> hits = await _Verbex.SearchAsync(indexId, query, max, ctx.Token).ConfigureAwait(false);

            SearchResponse response = new SearchResponse { Query = query };
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (VerbexHit hit in hits)
            {
                if (!hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || String.IsNullOrEmpty(nodeId)) continue;
                if (!seen.Add(nodeId)) continue;

                GraphNode? node = await _Graph.ReadNodeAsync(nodeId, ctx.Token).ConfigureAwait(false);
                if (node == null) continue;

                response.Results.Add(new SearchNodeResult { Node = node, Score = hit.Score, Snippet = hit.Snippet });
            }

            await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
        }

        private async Task SubjectSearchAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");

            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            if (subject == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Subject not found.").ConfigureAwait(false);
                return;
            }

            string? query = ctx.Request.Query.Elements?["q"];
            if (String.IsNullOrWhiteSpace(query))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A query (q) is required.").ConfigureAwait(false);
                return;
            }

            int maxResults = 20;
            string? maxText = ctx.Request.Query.Elements?["maxResults"];
            if (!String.IsNullOrEmpty(maxText) && Int32.TryParse(maxText, out int parsedMax)) maxResults = Math.Clamp(parsedMax, 1, 100);
            int skip = 0;
            string? skipText = ctx.Request.Query.Elements?["skip"];
            if (!String.IsNullOrEmpty(skipText) && Int32.TryParse(skipText, out int parsedSkip)) skip = Math.Max(0, parsedSkip);

            string indexId = await _Verbex.EnsureIndexAsync(ctx.Token).ConfigureAwait(false);
            Dictionary<string, string> filter = new Dictionary<string, string> { { "subjectId", subjectId } };
            List<VerbexHit> hits = await _Verbex.SearchAsync(indexId, query, 1000, filter, ctx.Token).ConfigureAwait(false);

            // Highest score first, then paginate in-process (Verbex returns the full ranked set).
            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
            int total = hits.Count;

            List<VerbexHit> pageHits = new List<VerbexHit>();
            for (int i = skip; i < hits.Count && pageHits.Count < maxResults; i++) pageHits.Add(hits[i]);

            // Resolve each hit back to its originating link (documentTag jobId -> job.LinkId -> link).
            Dictionary<string, string> jobToLink = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, SubjectLink> linkById = new Dictionary<string, SubjectLink>(StringComparer.Ordinal);
            foreach (VerbexHit hit in pageHits)
            {
                if (!hit.Tags.TryGetValue("jobId", out string? jobId) || String.IsNullOrEmpty(jobId)) continue;
                if (!jobToLink.ContainsKey(jobId))
                {
                    IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, jobId, ctx.Token).ConfigureAwait(false);
                    if (job != null) jobToLink[jobId] = job.LinkId;
                }
                if (jobToLink.TryGetValue(jobId, out string? linkId) && !String.IsNullOrEmpty(linkId) && !linkById.ContainsKey(linkId))
                {
                    SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, linkId, ctx.Token).ConfigureAwait(false);
                    if (link != null) linkById[linkId] = link;
                }
            }

            List<SubjectSearchResult> objects = new List<SubjectSearchResult>();
            foreach (VerbexHit hit in pageHits)
            {
                SubjectSearchResult result = new SubjectSearchResult
                {
                    DocumentId = hit.DocumentId,
                    Score = hit.Score,
                    Snippet = hit.Snippet
                };
                if (hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId)) result.NodeId = nodeId;
                if (hit.Tags.TryGetValue("jobId", out string? jid) && jobToLink.TryGetValue(jid, out string? lid))
                {
                    result.LinkId = lid;
                    if (linkById.TryGetValue(lid, out SubjectLink? link))
                    {
                        result.LinkUrl = link.Url;
                        result.LinkTitle = link.Title;
                    }
                }
                objects.Add(result);
            }

            EnumerationResult<SubjectSearchResult> envelope = new EnumerationResult<SubjectSearchResult>
            {
                Success = true,
                MaxResults = maxResults,
                Skip = skip,
                TotalRecords = total,
                RecordsRemaining = Math.Max(0, total - (skip + objects.Count)),
                EndOfResults = (skip + objects.Count) >= total,
                Objects = objects
            };
            await RouteHelper.SendJsonAsync(ctx, 200, envelope).ConfigureAwait(false);
        }

        #endregion
    }
}
