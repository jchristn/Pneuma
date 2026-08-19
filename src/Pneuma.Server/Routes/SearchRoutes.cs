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

            // A source link is indexed as many Verbex documents (one per chunk). Resolve every hit back to its
            // originating link (documentTag jobId -> job.LinkId) so chunk hits can be rolled up per link.
            Dictionary<string, string> jobToLink = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (VerbexHit hit in hits)
            {
                if (!hit.Tags.TryGetValue("jobId", out string? jobId) || String.IsNullOrEmpty(jobId)) continue;
                if (jobToLink.ContainsKey(jobId)) continue;
                IngestionJob? job = await _Db.IngestionJobs.ReadAsync(tenantId, jobId, ctx.Token).ConfigureAwait(false);
                if (job != null && !String.IsNullOrEmpty(job.LinkId)) jobToLink[jobId] = job.LinkId;
            }

            // Group hits into one result per source link (fall back to the Verbex document id when a link is
            // not resolvable). Each group keeps its best-scoring chunk and how many chunks matched.
            List<string> order = new List<string>();
            Dictionary<string, SearchGroup> groups = new Dictionary<string, SearchGroup>(StringComparer.Ordinal);
            foreach (VerbexHit hit in hits)
            {
                string? linkId = null;
                if (hit.Tags.TryGetValue("jobId", out string? jid) && !String.IsNullOrEmpty(jid)) jobToLink.TryGetValue(jid, out linkId);
                string key = !String.IsNullOrEmpty(linkId) ? "link:" + linkId : "doc:" + hit.DocumentId;

                if (!groups.TryGetValue(key, out SearchGroup? group))
                {
                    group = new SearchGroup { LinkId = linkId, Best = hit, MatchCount = 0 };
                    groups[key] = group;
                    order.Add(key);
                }
                group.MatchCount++;
                if (hit.Score > group.Best.Score) group.Best = hit;
            }

            List<SearchGroup> ranked = new List<SearchGroup>();
            foreach (string key in order) ranked.Add(groups[key]);
            ranked.Sort((a, b) => b.Best.Score.CompareTo(a.Best.Score));

            int total = ranked.Count;
            List<SearchGroup> pageGroups = new List<SearchGroup>();
            for (int i = skip; i < ranked.Count && pageGroups.Count < maxResults; i++) pageGroups.Add(ranked[i]);

            // Resolve link details (url/title) for the links shown on this page.
            Dictionary<string, SubjectLink> linkById = new Dictionary<string, SubjectLink>(StringComparer.Ordinal);
            foreach (SearchGroup group in pageGroups)
            {
                if (String.IsNullOrEmpty(group.LinkId) || linkById.ContainsKey(group.LinkId!)) continue;
                SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, group.LinkId!, ctx.Token).ConfigureAwait(false);
                if (link != null) linkById[group.LinkId!] = link;
            }

            List<SubjectSearchResult> objects = new List<SubjectSearchResult>();
            foreach (SearchGroup group in pageGroups)
            {
                SubjectSearchResult result = new SubjectSearchResult
                {
                    DocumentId = group.Best.DocumentId,
                    Score = group.Best.Score,
                    MatchCount = group.MatchCount,
                    Snippet = group.Best.Snippet,
                    LinkId = group.LinkId
                };
                if (group.Best.Tags.TryGetValue("litegraphNodeId", out string? nodeId)) result.NodeId = nodeId;
                if (!String.IsNullOrEmpty(group.LinkId) && linkById.TryGetValue(group.LinkId!, out SubjectLink? link))
                {
                    result.LinkUrl = link.Url;
                    result.LinkTitle = link.Title;
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

        #region Nested-Types

        /// <summary>Accumulates the chunk hits belonging to one source link while rolling up search results.</summary>
        private sealed class SearchGroup
        {
            /// <summary>The source content-link id, or null when the hit could not be resolved to a link.</summary>
            public string? LinkId { get; set; }

            /// <summary>The best-scoring chunk hit seen for this source.</summary>
            public VerbexHit Best { get; set; } = null!;

            /// <summary>How many chunk hits belong to this source.</summary>
            public int MatchCount { get; set; }
        }

        #endregion
    }
}
