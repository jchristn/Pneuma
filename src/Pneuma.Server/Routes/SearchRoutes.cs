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
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/subjects/{subjectId}/search/warmup", SubjectSearchWarmupAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Warm the subject's embedding model so the first search does not pay the cold-load cost", "Search"));
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

            string? query = RouteHelper.Query(ctx, "q");
            if (String.IsNullOrWhiteSpace(query))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A query (q) is required.").ConfigureAwait(false);
                return;
            }

            int max = 20;
            string? maxText = RouteHelper.Query(ctx, "max");
            if (!String.IsNullOrEmpty(maxText) && Int32.TryParse(maxText, out int parsed)) max = Math.Clamp(parsed, 1, 100);

            RetrievalModeEnum mode = ParseMode(ctx);
            string tenantId = rc.TenantId ?? String.Empty;
            RetrievalFilter? requestFilter = ParseFilter(ctx);
            string? collectionOverride = RouteHelper.Query(ctx, "collection");

            RetrievalOverrides? overrides = ParseOverrides(ctx, rc);
            List<RetrievedChunk> hits = await _Query.SearchAsync(tenantId, query!, max, null, mode, requestFilter, collectionOverride, ctx.Token, true, overrides).ConfigureAwait(false);

            SearchResponse response = new SearchResponse { Query = query!, Mode = mode.ToString() };
            foreach (RetrievedChunk hit in hits)
            {
                response.Results.Add(new SearchNodeResult
                {
                    Node = hit.Node,
                    Score = hit.Score,
                    Snippet = hit.Snippet,
                    LinkId = hit.LinkId,
                    DocumentId = hit.DocumentId,
                    FusedScore = hit.FusedScore,
                    VectorScore = hit.VectorScore,
                    TextScore = hit.TextScore,
                    VectorRank = hit.VectorRank,
                    TextRank = hit.TextRank,
                    ChunkKind = hit.ChunkKind,
                    Position = hit.Position
                });
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

            string? query = RouteHelper.Query(ctx, "q");
            if (String.IsNullOrWhiteSpace(query))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A query (q) is required.").ConfigureAwait(false);
                return;
            }

            int maxResults = 20;
            string? maxText = RouteHelper.Query(ctx, "maxResults");
            if (!String.IsNullOrEmpty(maxText) && Int32.TryParse(maxText, out int parsedMax)) maxResults = Math.Clamp(parsedMax, 1, 100);
            int skip = 0;
            string? skipText = RouteHelper.Query(ctx, "skip");
            if (!String.IsNullOrEmpty(skipText) && Int32.TryParse(skipText, out int parsedSkip)) skip = Math.Max(0, parsedSkip);

            RetrievalModeEnum mode = ParseMode(ctx);
            RetrievalFilter? requestFilter = ParseFilter(ctx);
            string? collectionOverride = RouteHelper.Query(ctx, "collection");

            // Gather a generous pool of hits (the subject default filter is merged in by the service) and roll
            // them up per source link. A source link is many chunk documents; each hit carries its link id.
            // resolveNodes:false — this view only needs each hit's link/snippet/score/document id (all carried on
            // the RecallDB hit), so we skip the per-hit LiteGraph node round-trip that otherwise dominated latency.
            RetrievalOverrides? overrides = ParseOverrides(ctx, rc);
            List<RetrievedChunk> hits = await _Query.SearchAsync(tenantId, query!, _Query.SearchPoolSize, subjectId, mode, requestFilter, collectionOverride, ctx.Token, false, overrides).ConfigureAwait(false);

            // granularity=chunk returns the ranked hits themselves (one per retrieved passage) instead of rolling
            // them up per source document, for passage-level evaluation.
            bool chunkLevel = String.Equals(RouteHelper.Query(ctx, "granularity"), "chunk", StringComparison.OrdinalIgnoreCase);

            List<string> order = new List<string>();
            Dictionary<string, SearchGroup> groups = new Dictionary<string, SearchGroup>(StringComparer.Ordinal);
            foreach (RetrievedChunk hit in hits)
            {
                string key = chunkLevel
                    ? "hit:" + hit.NodeId
                    : (!String.IsNullOrEmpty(hit.LinkId) ? "link:" + hit.LinkId : "doc:" + (hit.DocumentId ?? hit.NodeId));
                if (!groups.TryGetValue(key, out SearchGroup? group))
                {
                    group = new SearchGroup { LinkId = hit.LinkId, Best = hit, MatchCount = 0 };
                    groups[key] = group;
                    order.Add(key);
                }
                group.MatchCount++;
                // The hits arrive in fused (RRF) order, so the first hit of a document is its best one. Keep it:
                // comparing raw scores would mix cosine similarity and TsRank, which are not on one scale.
            }

            // Documents keep the fused order of their best hit. (Sorting by the raw Score, as before, ranked
            // hybrid results by whichever channel produced the larger number, not by the fused ranking.)
            List<SearchGroup> ranked = new List<SearchGroup>();
            foreach (string key in order) ranked.Add(groups[key]);

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
                    NodeId = group.Best.NodeId,
                    FusedScore = group.Best.FusedScore,
                    VectorScore = group.Best.VectorScore,
                    TextScore = group.Best.TextScore,
                    VectorRank = group.Best.VectorRank,
                    TextRank = group.Best.TextRank,
                    ChunkKind = group.Best.ChunkKind,
                    Position = group.Best.Position
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

        private async Task SubjectSearchWarmupAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = RouteHelper.Param(ctx, "subjectId");

            EmbeddingWarmupResult result = await _Query.WarmEmbeddingModelAsync(tenantId, subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
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
            string? raw = RouteHelper.Query(ctx, "mode");
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
        /// Parse per-request retrieval overrides from query parameters named like the fields of
        /// <see cref="RetrievalOverrides"/> (rrfK, lexicalWeight, semanticWeight, diversityEnabled,
        /// diversityLambda, poolMultiplier, neighborExpansionEnabled, neighborExpansionMaxHops,
        /// neighborExpansionMaxNodes). Honored only for system and tenant administrators, so ordinary callers
        /// cannot change retrieval cost or behavior.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rc">Request context.</param>
        /// <returns>The overrides, or null when none apply.</returns>
        private static RetrievalOverrides? ParseOverrides(HttpContextBase ctx, RequestContext rc)
        {
            if (!rc.IsAdmin && !rc.IsTenantAdmin) return null;
            RetrievalOverrides overrides = new RetrievalOverrides
            {
                RrfK = ParseInt(RouteHelper.Query(ctx, "rrfK")),
                LexicalWeight = ParseDouble(RouteHelper.Query(ctx, "lexicalWeight")),
                SemanticWeight = ParseDouble(RouteHelper.Query(ctx, "semanticWeight")),
                DiversityEnabled = ParseBool(RouteHelper.Query(ctx, "diversityEnabled")),
                DiversityLambda = ParseDouble(RouteHelper.Query(ctx, "diversityLambda")),
                PoolMultiplier = ParseInt(RouteHelper.Query(ctx, "poolMultiplier")),
                NeighborExpansionEnabled = ParseBool(RouteHelper.Query(ctx, "neighborExpansionEnabled")),
                NeighborExpansionMaxHops = ParseInt(RouteHelper.Query(ctx, "neighborExpansionMaxHops")),
                NeighborExpansionMaxNodes = ParseInt(RouteHelper.Query(ctx, "neighborExpansionMaxNodes"))
            };
            return overrides.IsEmpty() ? null : overrides;
        }

        private static int? ParseInt(string? raw)
        {
            return !String.IsNullOrWhiteSpace(raw) && Int32.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
        }

        private static double? ParseDouble(string? raw)
        {
            return !String.IsNullOrWhiteSpace(raw) && Double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double value) ? value : (double?)null;
        }

        private static bool? ParseBool(string? raw)
        {
            return !String.IsNullOrWhiteSpace(raw) && Boolean.TryParse(raw, out bool value) ? value : (bool?)null;
        }

        /// <summary>
        /// Parse the optional per-request facet filter from the <c>filter</c> query parameter (URL-encoded
        /// <see cref="RetrievalFilter"/> JSON). Returns null when absent or unparseable.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>The parsed filter, or null.</returns>
        private static RetrievalFilter? ParseFilter(HttpContextBase ctx)
        {
            string? raw = RouteHelper.Query(ctx, "filter");
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
