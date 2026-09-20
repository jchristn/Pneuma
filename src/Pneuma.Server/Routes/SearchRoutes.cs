namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// User-facing search over the retrieval store. Every mode — full-text only, vector only, or hybrid
    /// (RRF-fused) — is served by the shared <see cref="GroundedQueryService"/> so search, grounded answering,
    /// and the MCP tools cannot drift. The mode is selected with the <c>mode</c> query parameter
    /// (<c>text</c> | <c>vector</c> | <c>hybrid</c>); it defaults to hybrid.
    /// </summary>
    public class SearchRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly GroundedQueryService _Query;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate search routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="query">Shared retrieval/grounded-query service (provides full-text, vector, and hybrid search).</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public SearchRoutes(DatabaseDriverBase db, AuthorizationService authz, GroundedQueryService query)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (query == null) throw new ArgumentNullException(nameof(query));
            _Db = db;
            _Authz = authz;
            _Query = query;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/search", SearchAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Search the corpus (full-text, vector, or hybrid) for representative nodes", "Search"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/subjects/{subjectId}/search", SubjectSearchAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Search a subject's documents (full-text, vector, or hybrid), paginated by score", "Search"));
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

            RetrievalModeEnum mode = ParseMode(ctx);
            string tenantId = rc.TenantId ?? String.Empty;
            RetrievalFilter? requestFilter = ParseFilter(ctx);
            string? collectionOverride = ctx.Request.Query.Elements?["collection"];

            List<RetrievedChunk> hits = await _Query.SearchAsync(tenantId, query!, max, null, mode, requestFilter, collectionOverride, ctx.Token).ConfigureAwait(false);

            SearchResponse response = new SearchResponse { Query = query!, Mode = mode.ToString() };
            foreach (RetrievedChunk hit in hits)
            {
                response.Results.Add(new SearchNodeResult { Node = hit.Node, Score = hit.Score, Snippet = hit.Snippet });
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

            RetrievalModeEnum mode = ParseMode(ctx);
            RetrievalFilter? requestFilter = ParseFilter(ctx);
            string? collectionOverride = ctx.Request.Query.Elements?["collection"];

            // Gather a generous pool of hits (the subject default filter is merged in by the service) and roll
            // them up per source link. A source link is many chunk documents; each hit carries its link id.
            // resolveNodes:false — this view only needs each hit's link/snippet/score/document id (all carried on
            // the RecallDB hit), so we skip the per-hit LiteGraph node round-trip that otherwise dominated latency.
            List<RetrievedChunk> hits = await _Query.SearchAsync(tenantId, query!, 1000, subjectId, mode, requestFilter, collectionOverride, ctx.Token, false).ConfigureAwait(false);

            List<string> order = new List<string>();
            Dictionary<string, SearchGroup> groups = new Dictionary<string, SearchGroup>(StringComparer.Ordinal);
            foreach (RetrievedChunk hit in hits)
            {
                string key = !String.IsNullOrEmpty(hit.LinkId) ? "link:" + hit.LinkId : "doc:" + (hit.DocumentId ?? hit.NodeId);
                if (!groups.TryGetValue(key, out SearchGroup? group))
                {
                    group = new SearchGroup { LinkId = hit.LinkId, Best = hit, MatchCount = 0 };
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
                    DocumentId = group.Best.DocumentId ?? group.Best.NodeId,
                    Score = group.Best.Score,
                    MatchCount = group.MatchCount,
                    Snippet = group.Best.Snippet,
                    LinkId = group.LinkId,
                    NodeId = group.Best.NodeId
                };
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

        /// <summary>
        /// Parse the <c>mode</c> query parameter into a retrieval mode. Accepts <c>text</c>/<c>fulltext</c>/
        /// <c>keyword</c>/<c>lexical</c> for full-text, <c>vector</c>/<c>semantic</c> for vector, and anything
        /// else (including absent) as hybrid.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>The requested retrieval mode; hybrid by default.</returns>
        private static RetrievalModeEnum ParseMode(HttpContextBase ctx)
        {
            string? raw = ctx.Request.Query.Elements?["mode"];
            if (String.IsNullOrWhiteSpace(raw)) return RetrievalModeEnum.Hybrid;
            switch (raw!.Trim().ToLowerInvariant())
            {
                case "text":
                case "fulltext":
                case "full-text":
                case "keyword":
                case "lexical":
                    return RetrievalModeEnum.FullText;
                case "vector":
                case "semantic":
                case "embedding":
                    return RetrievalModeEnum.Vector;
                default:
                    return RetrievalModeEnum.Hybrid;
            }
        }

        /// <summary>
        /// Parse the optional per-request facet filter from the <c>filter</c> query parameter (URL-encoded
        /// <see cref="RetrievalFilter"/> JSON). Returns null when absent or unparseable.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>The parsed filter, or null.</returns>
        private static RetrievalFilter? ParseFilter(HttpContextBase ctx)
        {
            string? raw = ctx.Request.Query.Elements?["filter"];
            if (String.IsNullOrWhiteSpace(raw)) return null;
            try
            {
                RetrievalFilter? filter = Json.Deserialize<RetrievalFilter>(raw!);
                return (filter == null || filter.IsEmpty()) ? null : filter;
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion

        #region Nested-Types

        /// <summary>Accumulates the chunk hits belonging to one source link while rolling up search results.</summary>
        private sealed class SearchGroup
        {
            /// <summary>The source content-link id, or null when the hit could not be resolved to a link.</summary>
            public string? LinkId { get; set; }

            /// <summary>The best-scoring chunk hit seen for this source.</summary>
            public RetrievedChunk Best { get; set; } = null!;

            /// <summary>How many chunk hits belong to this source.</summary>
            public int MatchCount { get; set; }
        }

        #endregion
    }
}
