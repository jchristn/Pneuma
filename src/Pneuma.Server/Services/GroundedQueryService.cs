namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Settings;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Grounded question answering shared by the REST query routes and the MCP query tool: retrieves
    /// supporting nodes (lexical + semantic vector search, plus optional graph-neighbor expansion) and
    /// synthesizes a cited answer from the tenant's answering model. Keeping this in one service means
    /// the REST and MCP surfaces cannot drift.
    /// </summary>
    public class GroundedQueryService
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IInvertedIndex _Search;
        private readonly ICollectionStore _Collections;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly IVectorRepository _Vectors;
        private readonly ISemanticProcessor _Processor;
        private readonly RetrievalSettings _Retrieval;
        private readonly Aes256Cipher _Cipher;
        private readonly ICrossEncoderReranker? _CrossEncoder;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the grounded query service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository (RecallDB).</param>
        /// <param name="processor">Semantic processor (query embedding).</param>
        /// <param name="retrieval">Retrieval settings.</param>
        /// <param name="cipher">Cipher for decrypting model-runner keys.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="crossEncoder">Optional cross-encoder reranker; when null (or unconfigured) reranking uses the LLM listwise path.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public GroundedQueryService(
            DatabaseDriverBase db,
            IInvertedIndex search,
            ICollectionStore collections,
            IGraphRepositoryFactory graphFactory,
            IVectorRepository vectors,
            ISemanticProcessor processor,
            RetrievalSettings retrieval,
            Aes256Cipher cipher,
            LoggingModule logging,
            ICrossEncoderReranker? crossEncoder = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (vectors == null) throw new ArgumentNullException(nameof(vectors));
            if (processor == null) throw new ArgumentNullException(nameof(processor));
            if (retrieval == null) throw new ArgumentNullException(nameof(retrieval));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Search = search;
            _Collections = collections;
            _GraphFactory = graphFactory;
            _Vectors = vectors;
            _Processor = processor;
            _Retrieval = retrieval;
            _Cipher = cipher;
            _CrossEncoder = crossEncoder;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The number of candidate chunks the subject-search endpoint should over-fetch before grouping hits
        /// by source link and paginating. Sourced from <see cref="RetrievalSettings.SearchPoolSize"/>.
        /// </summary>
        public int SearchPoolSize
        {
            get { return _Retrieval.SearchPoolSize; }
        }

        /// <summary>
        /// Warm a subject's embedding model by issuing a trivial embed, so a model provider that unloads idle
        /// models (e.g. Ollama) reloads it before the user's first search rather than during it. Idempotent and
        /// cheap once the model is resident. Intended to be called when the operator selects a subject to search.
        /// </summary>
        /// <param name="tenantId">Tenant that owns the subject.</param>
        /// <param name="subjectId">Subject whose embedding model should be warmed.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The warm-up outcome, including the model's display name and how long the embed took.</returns>
        public async Task<EmbeddingWarmupResult> WarmEmbeddingModelAsync(string tenantId, string subjectId, CancellationToken token = default)
        {
            EmbeddingWarmupResult result = new EmbeddingWarmupResult();

            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            if (subject == null) return result;

            string? modelId = subject.EmbeddingModel;
            result.ModelId = modelId;
            if (!String.IsNullOrEmpty(modelId))
            {
                ModelRunner? runner = await _Db.ModelRunners.ReadAsync(modelId!, token).ConfigureAwait(false);
                result.ModelName = runner?.Name ?? modelId;
            }

            DateTime started = DateTime.UtcNow;
            List<float>? embedding = await EmbedQueryAsync("warmup", modelId, token).ConfigureAwait(false);
            result.ElapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds;
            result.Ready = embedding != null && embedding.Count > 0;
            result.Success = true;
            return result;
        }

        /// <summary>Answer a grounded question end to end (non-streaming).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="question">The question.</param>
        /// <param name="max">Maximum sources to retrieve.</param>
        /// <param name="subjectId">Optional subject to scope retrieval to; null searches the whole tenant.</param>
        /// <param name="citedLinkScores">Optional sink mapping each cited content-link id to the best relevance score of its chunks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="overrides">Optional per-request retrieval overrides (administrators only; validated by the caller).</param>
        /// <returns>The grounded answer.</returns>
        public async Task<GroundedAnswer> AnswerAsync(string tenantId, string question, int max, string? subjectId, IDictionary<string, double>? citedLinkScores, RetrievalFilter? requestFilter = null, CancellationToken token = default, RetrievalOverrides? overrides = null)
        {
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            List<GraphNode> sources = await RetrieveForAnswerAsync(tenantId, subject, question, max, subjectId, citedLinkScores, requestFilter, token, overrides).ConfigureAwait(false);
            if (sources.Count == 0)
            {
                return new GroundedAnswer
                {
                    Answer = "The archive does not contain enough information to answer that question.",
                    Grounded = false,
                    InsufficientSupport = true
                };
            }

            ModelRunner? runner = await ResolveAnswerRunnerAsync(tenantId, subject, token).ConfigureAwait(false);
            if (runner == null)
            {
                return new GroundedAnswer
                {
                    Answer = "No answering model is configured. The returned sources are relevant to your question.",
                    Sources = sources,
                    Grounded = true
                };
            }

            Stopwatch stage = Stopwatch.StartNew();
            GeneratedAnswer generated = await GenerateAnswerDetailedAsync(question, sources, tenantId, runner, subjectId, token).ConfigureAwait(false);
            PneumaMetrics.RecordRetrievalStage("generate", stage.Elapsed.TotalSeconds);
            return new GroundedAnswer
            {
                Answer = generated.Text,
                Sources = sources,
                Grounded = true,
                AnswerModel = generated.Model,
                GenerationMs = generated.DurationMs
            };
        }

        /// <summary>
        /// Retrieve the grounding sources for an answer: the optional prompt rewrite (the rewritten form drives
        /// retrieval and reranking; the answer still addresses the original question), hybrid retrieval with
        /// diversity selection and optional neighbor expansion, then the optional rerank. Shared by the
        /// non-streaming and streaming answer routes so they ground on the same passages.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">The subject in scope (already loaded), or null.</param>
        /// <param name="question">The user's question.</param>
        /// <param name="max">Maximum primary sources.</param>
        /// <param name="subjectId">Subject id to scope retrieval to, or null for the whole tenant.</param>
        /// <param name="citedLinkScores">Optional sink mapping each cited content-link id to its best relevance score.</param>
        /// <param name="requestFilter">Optional per-request facet filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="overrides">Optional per-request retrieval overrides.</param>
        /// <returns>The sources in presentation order (empty when nothing was retrieved).</returns>
        public async Task<List<GraphNode>> RetrieveForAnswerAsync(string tenantId, Subject? subject, string question, int max, string? subjectId, IDictionary<string, double>? citedLinkScores, RetrievalFilter? requestFilter = null, CancellationToken token = default, RetrievalOverrides? overrides = null)
        {
            Stopwatch stage = Stopwatch.StartNew();
            string retrievalQuestion = await RewriteQuestionAsync(tenantId, subject, question, token).ConfigureAwait(false);
            if (!ReferenceEquals(retrievalQuestion, question)) PneumaMetrics.RecordRetrievalStage("rewrite", stage.Elapsed.TotalSeconds);

            List<GraphNode> sources = await RetrieveSourcesAsync(tenantId, retrievalQuestion, max, subjectId, citedLinkScores, requestFilter, token, overrides).ConfigureAwait(false);
            if (sources.Count == 0) return sources;

            stage.Restart();
            List<GraphNode> reranked = await RerankAsync(tenantId, subject, retrievalQuestion, sources, NodeText, token).ConfigureAwait(false);
            if (!ReferenceEquals(reranked, sources)) PneumaMetrics.RecordRetrievalStage("rerank", stage.Elapsed.TotalSeconds);
            return reranked;
        }

        /// <summary>
        /// Load a subject by id (null for no subject), for callers that need it for both retrieval and runner
        /// resolution.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject id, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject, or null.</returns>
        public async Task<Subject?> ReadSubjectAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(subjectId)) return null;
            return await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Answer a global / thematic question about a subject from its community summaries instead of
        /// chunk-level retrieval ("what are the main themes across this subject?"). The subject's community
        /// summaries are ranked by relevance to the question, the strongest are used as grounding, and a cited
        /// answer is synthesized. Returns an insufficient-support answer when no community summaries exist yet
        /// (they must be built first).
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to answer about.</param>
        /// <param name="question">The thematic question.</param>
        /// <param name="max">Maximum community summaries to ground on.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer.</returns>
        public async Task<GroundedAnswer> AnswerGlobalAsync(string tenantId, string subjectId, string question, int max, CancellationToken token = default)
        {
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);

            Dictionary<string, string> summaryTags = new Dictionary<string, string>
            {
                { Ontology.TagSubjectId, subjectId },
                { Ontology.TagNodeType, Ontology.NodeCommunitySummary }
            };
            List<GraphNode> summaries = await graph.SearchNodesByTagsAsync(summaryTags, 1000, token).ConfigureAwait(false);
            if (summaries.Count == 0)
            {
                return new GroundedAnswer
                {
                    Answer = "This subject has no community summaries yet, so a thematic overview can't be produced. Build communities for the subject first.",
                    Grounded = false,
                    InsufficientSupport = true
                };
            }

            // Rank community summaries by lexical overlap with the question (no embedding round-trip needed for a
            // handful of summaries), and ground on the strongest few.
            Dictionary<string, int> questionFreq = TokenFrequencies(question);
            summaries.Sort((GraphNode a, GraphNode b) =>
            {
                double sa = CosineSimilarity(TokenFrequencies(a.Content ?? String.Empty), questionFreq);
                double sb = CosineSimilarity(TokenFrequencies(b.Content ?? String.Empty), questionFreq);
                int byScore = sb.CompareTo(sa);
                return byScore != 0 ? byScore : String.CompareOrdinal(a.Id, b.Id);
            });
            if (summaries.Count > max) summaries = summaries.GetRange(0, max);

            ModelRunner? runner = await ResolveAnswerRunnerAsync(tenantId, subject, token).ConfigureAwait(false);
            if (runner == null)
            {
                return new GroundedAnswer
                {
                    Answer = "No answering model is configured. The returned community summaries are relevant to your question.",
                    Sources = summaries,
                    Grounded = true
                };
            }

            GeneratedAnswer generated = await GenerateAnswerDetailedAsync(question, summaries, tenantId, runner, subjectId, token).ConfigureAwait(false);
            return new GroundedAnswer
            {
                Answer = generated.Text,
                Sources = summaries,
                Grounded = true,
                AnswerModel = generated.Model,
                GenerationMs = generated.DurationMs
            };
        }

        /// <summary>Retrieve supporting nodes for a question (lexical + vector + optional neighbor expansion).</summary>
        /// <param name="tenantId">Tenant whose RecallDB collection is searched.</param>
        /// <param name="question">The question.</param>
        /// <param name="max">Maximum primary sources.</param>
        /// <param name="subjectId">Optional subject to scope retrieval to; null searches the whole tenant.</param>
        /// <param name="citedLinkScores">Optional sink mapping each cited content-link id to the best relevance score of its chunks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The supporting nodes.</returns>
        /// <summary>
        /// Resolve a subject's default retrieval facet filter, or null when the subject has none. Used by the
        /// agentic search tool so it applies the same facets as the grounded path.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject's default filter, or null when absent or empty.</returns>
        public async Task<RetrievalFilter?> GetSubjectFilterAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(subjectId)) return null;
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            if (subject == null || String.IsNullOrWhiteSpace(subject.RetrievalFilterJson)) return null;
            RetrievalFilter merged = MergeFilters(subject.RetrievalFilterJson, null);
            return merged.IsEmpty() ? null : merged;
        }

        /// <summary>
        /// Resolve the effective retrieval filter for a request: the subject's default filter merged with a
        /// per-request filter (union of required and excluded, so the request narrows — never widens — the
        /// default). Used by the agentic chat path so a per-turn filter scopes the search tool the same way the
        /// grounded path scopes REST retrieval.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier, or null.</param>
        /// <param name="requestFilter">The per-request filter, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merged effective filter, or null when it carries no predicate.</returns>
        public async Task<RetrievalFilter?> ResolveEffectiveFilterAsync(string tenantId, string? subjectId, RetrievalFilter? requestFilter, CancellationToken token = default)
        {
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            RetrievalFilter merged = MergeFilters(subject?.RetrievalFilterJson, requestFilter);
            return merged.IsEmpty() ? null : merged;
        }

        /// <summary>
        /// Warm the subject's answering model by sending a minimal completion, so the first real question does
        /// not pay the model's cold-load cost (e.g. Ollama loading weights into memory). Best-effort and
        /// bounded: resolves the runner, sends a tiny prompt, and swallows any failure.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject whose answering model to warm, or null for the tenant default.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when a warm-up completion was issued to a resolved runner.</returns>
        public async Task<bool> WarmupAsync(string tenantId, string? subjectId, CancellationToken token = default)
        {
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            ModelRunner? runner = await ResolveAnswerRunnerAsync(tenantId, subject, token).ConfigureAwait(false);
            if (runner == null) return false;
            await CompleteTextAsync(runner, "You are warming up. Reply with the single word: ok.", "ok", 1, token).ConfigureAwait(false);
            return true;
        }

        /// <summary>
        /// Run a one-shot completion using a subject's answering model (its inference model, or the tenant
        /// default). Used by the evaluation harness to judge a produced answer against a ground-truth answer.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">Subject in scope, or null.</param>
        /// <param name="systemPrompt">The system prompt.</param>
        /// <param name="userText">The user text.</param>
        /// <param name="maxTokens">Maximum completion tokens.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The completion text, or null when no model is available.</returns>
        public async Task<string?> CompleteWithSubjectAsync(string tenantId, Subject? subject, string systemPrompt, string userText, int maxTokens, CancellationToken token = default)
        {
            ModelRunner? runner = await ResolveAnswerRunnerAsync(tenantId, subject, token).ConfigureAwait(false);
            if (runner == null) return null;
            return await CompleteTextAsync(runner, systemPrompt, userText, maxTokens, token).ConfigureAwait(false);
        }

        public async Task<List<GraphNode>> RetrieveSourcesAsync(string tenantId, string question, int max, string? subjectId, IDictionary<string, double>? citedLinkScores, RetrievalFilter? requestFilter = null, CancellationToken token = default, RetrievalOverrides? overrides = null)
        {
            RetrievalKnobs knobs = new RetrievalKnobs(_Retrieval, overrides);

            // Gather + fuse the lexical and vector channels (RRF) over a candidate pool wider than the final
            // `max`, so fusion and diversity selection have alternatives to choose among rather than being
            // limited to the top-`max` of each channel. Then select the passages to ground on.
            int poolSize = Math.Clamp(max * knobs.PoolMultiplier, max, 200);
            RetrievalPool pool = await GatherAsync(tenantId, question, poolSize, subjectId, RetrievalModeEnum.Hybrid, requestFilter, null, true, knobs, token).ConfigureAwait(false);
            if (pool.OrderedIds.Count == 0) return new List<GraphNode>();
            Dictionary<string, int> positionByNode = pool.PositionByNode;
            IGraphRepository graph = pool.Graph!;

            // Select up to `max` passages. With diversity enabled, use Maximal Marginal Relevance so
            // near-duplicate chunks do not crowd the grounding context — the answer sees diverse support rather
            // than the same point repeated; otherwise take the top `max` by fused score.
            Stopwatch stage = Stopwatch.StartNew();
            List<string> selectedIds = knobs.DiversityEnabled
                ? SelectWithMmr(pool, max, knobs.DiversityLambda)
                : (pool.OrderedIds.Count > max ? pool.OrderedIds.GetRange(0, max) : pool.OrderedIds);
            PneumaMetrics.RecordRetrievalStage(knobs.DiversityEnabled ? "mmr" : "select", stage.Elapsed.TotalSeconds);

            List<GraphNode> primary = new List<GraphNode>(selectedIds.Count);
            Dictionary<string, double> scoreByNode = new Dictionary<string, double>(StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in selectedIds)
            {
                GraphNode selected = pool.NodeById[id];
                // Provenance for callers: the originating content link travels on the returned source as a
                // linkId tag (graph nodes carry the asserting job and Source node, not the link).
                if (pool.LinkByNode.TryGetValue(id, out string? sourceLink) && !String.IsNullOrEmpty(sourceLink))
                {
                    if (selected.Tags == null) selected.Tags = new Dictionary<string, string>(StringComparer.Ordinal);
                    selected.Tags["linkId"] = sourceLink!;
                }
                primary.Add(selected);
                // Reconstruction orders source-document groups by their best hit; use the fused RRF score so the
                // most strongly-supported document leads.
                scoreByNode[id] = pool.RrfByNode.TryGetValue(id, out double r) ? r : 0.0;
                seen.Add(id);
                // Record citation relevance only for the passages actually selected for grounding (not the whole
                // candidate pool), so a link is cited only when its content reaches the answer.
                if (citedLinkScores != null && pool.LinkByNode.TryGetValue(id, out string? linkId) && !String.IsNullOrEmpty(linkId))
                {
                    RecordCitationScore(citedLinkScores, linkId!, pool.BestRawByNode.TryGetValue(id, out double raw) ? raw : 0.0);
                }
            }

            // RecallDB owns ordering and reconstruction: group the retrieved chunks by their source document and
            // present each group's chunks in stored position order, with the most relevant source first. This
            // reads material back the way it was written rather than in raw hit order.
            List<GraphNode> sources = OrderForReconstruction(primary, positionByNode, scoreByNode);

            // The graph owns structure, relationships, and source/citation references: expand each retrieved
            // chunk to the entities it is connected to and to its Source node, adding that context (not more raw
            // text) to the grounding set. Enabled via NeighborExpansion settings.
            if (knobs.NeighborExpansionEnabled && sources.Count > 0)
            {
                stage.Restart();
                int ceiling = max + knobs.NeighborExpansionMaxNodes;
                int hops = knobs.NeighborExpansionMaxHops;
                try
                {
                    List<GraphNode> seeds = new List<GraphNode>(sources);
                    foreach (GraphNode seed in seeds)
                    {
                        if (sources.Count >= ceiling) break;
                        if (String.IsNullOrEmpty(seed.Id)) continue;

                        // Single hop: a direct-neighbor read. Multiple hops: a bounded server-side subgraph
                        // extraction that reaches entities several relationships away (LiteGraph does the BFS).
                        List<GraphNode> reached;
                        if (hops <= 1)
                        {
                            reached = await graph.GetNeighborsAsync(seed.Id, token).ConfigureAwait(false);
                        }
                        else
                        {
                            GraphSubgraph subgraph = await graph.GetSubgraphAsync(seed.Id, hops, ceiling, 0, token).ConfigureAwait(false);
                            reached = subgraph.Nodes;
                        }

                        foreach (GraphNode neighbor in reached)
                        {
                            if (String.IsNullOrEmpty(neighbor.Id) || !seen.Add(neighbor.Id)) continue;
                            // Only structural neighbors add value here: entities (relationships) and the Source
                            // (citation). Sibling chunk nodes carry no content of their own — their text is in
                            // RecallDB and would only be surfaced by a direct retrieval hit. Community-summary
                            // nodes are thematic overviews, not local grounding, so they are excluded here too.
                            if (String.Equals(neighbor.NodeType, Ontology.NodeChunk, StringComparison.Ordinal)) continue;
                            if (String.Equals(neighbor.NodeType, Ontology.NodeCommunitySummary, StringComparison.Ordinal)) continue;
                            sources.Add(neighbor);
                            if (sources.Count >= ceiling) break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    _Logging.Warn("[GroundedQueryService] neighbor expansion failed: " + exception.Message);
                }

                PneumaMetrics.RecordRetrievalStage("neighbor_expand", stage.Elapsed.TotalSeconds);
            }

            return sources;
        }

        /// <summary>
        /// Run a ranked search over the retrieval store in the requested mode — full-text only, vector only, or
        /// hybrid (RRF-fused) — returning scored chunk hits (no neighbor expansion or diversity selection, so it
        /// is a faithful search rather than a grounding set). Shared by the REST search routes so every mode
        /// returns one shape.
        /// </summary>
        /// <param name="tenantId">Tenant whose collection is searched.</param>
        /// <param name="question">The query text.</param>
        /// <param name="max">Maximum hits to return.</param>
        /// <param name="subjectId">Optional subject to scope retrieval to; null searches the whole tenant.</param>
        /// <param name="mode">Which channels to use.</param>
        /// <param name="requestFilter">Optional per-request facet filter (merged with the subject default).</param>
        /// <param name="collectionOverride">Optional explicit collection id; overrides the subject/default resolution.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="resolveNodes">False skips the per-hit graph read and returns lightweight nodes built from the hit.</param>
        /// <param name="overrides">Optional per-request retrieval overrides (administrators only; validated by the caller).</param>
        /// <returns>The ranked hits, most relevant first.</returns>
        public async Task<List<RetrievedChunk>> SearchAsync(string tenantId, string question, int max, string? subjectId, RetrievalModeEnum mode, RetrievalFilter? requestFilter = null, string? collectionOverride = null, CancellationToken token = default, bool resolveNodes = true, RetrievalOverrides? overrides = null)
        {
            List<RetrievedChunk> results = new List<RetrievedChunk>();
            if (String.IsNullOrWhiteSpace(question)) return results;

            RetrievalKnobs knobs = new RetrievalKnobs(_Retrieval, overrides);
            RetrievalPool pool = await GatherAsync(tenantId, question, max, subjectId, mode, requestFilter, collectionOverride, resolveNodes, knobs, token).ConfigureAwait(false);

            foreach (string id in pool.OrderedIds)
            {
                if (results.Count >= max) break;
                GraphNode node = pool.NodeById[id];
                // Report the actual relevance score the retrieval store returned for the hit — cosine similarity
                // for the vector channel, TsRank for full-text — taking the best across whichever channels
                // matched this node (BestRawByNode). This holds for every mode, including hybrid: Reciprocal-Rank
                // Fusion (pool.OrderedIds) is used only to *order* the fused list because it is scale-independent,
                // but its fused value is tiny by construction (~1/(k+rank)) and meaningless as a displayed score,
                // so the surfaced score stays the real retrieval-store relevance.
                double score = pool.BestRawByNode.TryGetValue(id, out double s) ? s : 0.0;
                double rrf = pool.RrfByNode.TryGetValue(id, out double fused) ? fused : 0.0;
                results.Add(new RetrievedChunk
                {
                    Node = node,
                    NodeId = id,
                    Score = score,
                    FusedScore = pool.FusedMax > 0.0 ? Math.Round(rrf / pool.FusedMax, 6) : 0.0,
                    VectorScore = pool.VectorScoreByNode.TryGetValue(id, out double vs) ? vs : (double?)null,
                    TextScore = pool.TextScoreByNode.TryGetValue(id, out double ts) ? ts : (double?)null,
                    VectorRank = pool.VectorRankByNode.TryGetValue(id, out int vr) ? vr : (int?)null,
                    TextRank = pool.TextRankByNode.TryGetValue(id, out int tr) ? tr : (int?)null,
                    ChunkKind = pool.ChunkKindByNode.TryGetValue(id, out string? kind) ? kind : null,
                    Position = pool.PositionByNode.TryGetValue(id, out int position) ? position : 0,
                    Snippet = pool.SnippetByNode.TryGetValue(id, out string? sn) ? sn : node.Content,
                    LinkId = pool.LinkByNode.TryGetValue(id, out string? l) ? l : null,
                    DocumentId = pool.DocIdByNode.TryGetValue(id, out string? d) ? d : null
                });
            }
            return results;
        }

        /// <summary>
        /// Resolve the tenant's answering model runner. Prefers an explicitly-configured Pneuma model runner
        /// marked for user prompts; when none exists, falls back to the tenant's configured completion
        /// endpoint (what the Model Runners dashboard manages) so answering works out of the box with the
        /// completion model, without requiring a separately-created runner.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An answering runner, or null when no completion model is configured anywhere.</returns>
        public async Task<ModelRunner?> ResolveAnswerRunnerAsync(string tenantId, CancellationToken token = default)
        {
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (!runner.Active) continue;
                if (runner.Usage == ModelRunnerUsageEnum.UserPrompt || runner.Usage == ModelRunnerUsageEnum.Both) return runner;
            }

            return await ResolveDefaultCompletionRunnerAsync(token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve the answering runner for a subject: prefer the subject's configured inference model (a
        /// completion endpoint), falling back to the tenant-global resolution when the subject has none.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">Subject in scope, or null for a general (non-subject) chat.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An answering runner, or null when none is configured.</returns>
        public async Task<ModelRunner?> ResolveAnswerRunnerAsync(string tenantId, Subject? subject, CancellationToken token = default)
        {
            if (subject != null && !String.IsNullOrWhiteSpace(subject.InferenceModel))
            {
                ModelRunner? resolved = await ResolveCompletionRunnerByIdAsync(subject.InferenceModel!, token).ConfigureAwait(false);
                if (resolved != null) return resolved;
            }
            return await ResolveAnswerRunnerAsync(tenantId, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve a specific completion model-runner id into its stored <see cref="ModelRunner"/> (used for a
        /// subject's inference, reranking, and prompt-rewrite models). Returns null when the runner is unknown
        /// or inactive.
        /// </summary>
        /// <param name="endpointId">Model runner id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored runner, or null.</returns>
        public async Task<ModelRunner?> ResolveCompletionRunnerByIdAsync(string endpointId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) return null;
            try
            {
                ModelRunner? runner = await _Db.ModelRunners.ReadAsync(endpointId, token).ConfigureAwait(false);
                if (runner == null || !runner.Active) return null;
                return runner;
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] could not read completion runner '" + endpointId + "': " + exception.Message);
                return null;
            }
        }

        /// <summary>
        /// Rewrite a question into a retrieval query using the subject's prompt-rewrite model, when configured.
        /// Returns the original question unchanged when no rewrite model is set or the call fails.
        /// </summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">Subject in scope (may be null).</param>
        /// <param name="question">The user's question.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rewritten query, or the original question.</returns>
        public async Task<string> RewriteQuestionAsync(string tenantId, Subject? subject, string question, CancellationToken token = default)
        {
            if (subject == null || String.IsNullOrWhiteSpace(subject.PromptRewriteModel) || String.IsNullOrWhiteSpace(question)) return question;
            ModelRunner? runner = await ResolveCompletionRunnerByIdAsync(subject.PromptRewriteModel!, token).ConfigureAwait(false);
            if (runner == null) return question;
            string systemPrompt = await MergePromptAsync(tenantId, subject.Id, "prompt.rewrite", subject.PromptRewritePrompt, token).ConfigureAwait(false);
            // Ground the rewrite in the subject so vague questions ("tell me more about the side effects") resolve
            // to it ("...of {subject}") rather than the rewrite model inventing a placeholder subject.
            if (!String.IsNullOrWhiteSpace(subject.DisplayName))
            {
                systemPrompt = systemPrompt +
                    "\n\nThe question is about the subject \"" + subject.DisplayName.Trim() + "\". Resolve vague or " +
                    "pronoun references (e.g. \"it\", \"they\", \"the side effects\") to this subject. Never introduce a " +
                    "placeholder or hypothetical name; if the subject is not explicitly named, assume the question is about \"" +
                    subject.DisplayName.Trim() + "\".";
            }
            string? rewritten = await CompleteTextAsync(runner, systemPrompt, question, 256, token).ConfigureAwait(false);
            return String.IsNullOrWhiteSpace(rewritten) ? question : rewritten!;
        }

        /// <summary>
        /// Re-rank candidate passages by relevance to the question using the subject's reranking model, when
        /// configured. Returns the candidates in their original order when no reranking model is set, there is
        /// nothing to reorder, or the model output cannot be parsed into a complete ordering.
        /// </summary>
        /// <typeparam name="T">Candidate type.</typeparam>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subject">Subject in scope (may be null).</param>
        /// <param name="question">The (possibly rewritten) question.</param>
        /// <param name="candidates">Candidates to reorder.</param>
        /// <param name="textOf">Extracts the text used to judge a candidate's relevance.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reordered candidates (or the original list).</returns>
        public async Task<List<T>> RerankAsync<T>(string tenantId, Subject? subject, string question, List<T> candidates, Func<T, string> textOf, CancellationToken token = default)
        {
            if (candidates == null || candidates.Count <= 1) return candidates ?? new List<T>();

            // Dedicated cross-encoder path (per-subject opt-in): score each passage and reorder by score. Falls
            // through to the LLM listwise path below when it is unconfigured or the call fails.
            if (subject != null && subject.RerankerType == RerankerTypeEnum.CrossEncoder && _CrossEncoder != null && _CrossEncoder.IsConfigured)
            {
                List<T>? reordered = await RerankWithCrossEncoderAsync(question, candidates, textOf, token).ConfigureAwait(false);
                if (reordered != null) return reordered;
            }

            if (subject == null || String.IsNullOrWhiteSpace(subject.RerankingModel)) return candidates;
            ModelRunner? runner = await ResolveCompletionRunnerByIdAsync(subject.RerankingModel!, token).ConfigureAwait(false);
            if (runner == null) return candidates;
            string systemPrompt = await MergePromptAsync(tenantId, subject.Id, "reranking", subject.RerankingPrompt, token).ConfigureAwait(false);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Question: " + question);
            sb.AppendLine();
            sb.AppendLine("Passages:");
            for (int i = 0; i < candidates.Count; i++)
            {
                string text = textOf(candidates[i]) ?? String.Empty;
                if (text.Length > 500) text = text.Substring(0, 500);
                sb.AppendLine("[" + (i + 1) + "] " + text.Replace("\r", " ").Replace("\n", " "));
            }

            string? ranked = await CompleteTextAsync(runner, systemPrompt, sb.ToString(), 128, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(ranked)) return candidates;

            List<T> ordered = new List<T>(candidates.Count);
            HashSet<int> used = new HashSet<int>();
            foreach (Match match in Regex.Matches(ranked, "\\d+"))
            {
                if (Int32.TryParse(match.Value, out int n) && n >= 1 && n <= candidates.Count && used.Add(n)) ordered.Add(candidates[n - 1]);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                if (!used.Contains(i + 1)) ordered.Add(candidates[i]);
            }
            return ordered.Count == candidates.Count ? ordered : candidates;
        }

        /// <summary>
        /// Rerank candidates with the configured cross-encoder: score each passage against the question and
        /// reorder by descending score (stable on ties). Returns null when the reranker returns no usable
        /// scores, so the caller can fall back to LLM listwise reranking.
        /// </summary>
        private async Task<List<T>?> RerankWithCrossEncoderAsync<T>(string question, List<T> candidates, Func<T, string> textOf, CancellationToken token)
        {
            List<string> passages = new List<string>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                string text = textOf(candidates[i]) ?? String.Empty;
                if (text.Length > 2000) text = text.Substring(0, 2000);
                passages.Add(text);
            }

            IReadOnlyList<double>? scores = await _CrossEncoder!.ScoreAsync(question, passages, token).ConfigureAwait(false);
            if (scores == null || scores.Count != candidates.Count) return null;

            List<int> order = new List<int>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) order.Add(i);
            order.Sort((int a, int b) =>
            {
                int byScore = scores[b].CompareTo(scores[a]);
                return byScore != 0 ? byScore : a.CompareTo(b);
            });

            List<T> ordered = new List<T>(candidates.Count);
            foreach (int index in order) ordered.Add(candidates[index]);
            return ordered;
        }

        /// <summary>
        /// Convenience overload that loads the subject by id and reranks (for callers that hold a subject id but
        /// not the subject, e.g. the agentic search tool).
        /// </summary>
        public async Task<List<T>> RerankAsync<T>(string tenantId, string? subjectId, string question, List<T> candidates, Func<T, string> textOf, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(subjectId)) return candidates ?? new List<T>();
            Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            return await RerankAsync(tenantId, subject, question, candidates, textOf, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve a prompt's effective content for a subject: the global default combined with any per-subject
        /// override (Append/Replace) and the legacy per-subject prompt column. Global is the fallback.
        /// </summary>
        private async Task<string> MergePromptAsync(string tenantId, string? subjectId, string key, string? legacyOverride, CancellationToken token)
        {
            PromptResolver resolver = new PromptResolver(_Db);
            ResolvedPrompt resolved = await resolver.ResolveAsync(tenantId, subjectId, key, legacyOverride, token).ConfigureAwait(false);
            return resolved.EffectiveContent;
        }

        /// <summary>Run a single text completion against a runner and return the trimmed text, or null on failure.</summary>
        private async Task<string?> CompleteTextAsync(ModelRunner runner, string systemPrompt, string userText, int maxTokens, CancellationToken token)
        {
            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }
            try
            {
                CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
                ChatCompletionOptions options = new ChatCompletionOptions { Temperature = 0.1, MaxTokens = maxTokens, SystemPrompt = systemPrompt };
                ChatResponse response = await client.ChatAsync(userText, options, token).ConfigureAwait(false);
                if (response != null && response.Success && !String.IsNullOrWhiteSpace(response.Text)) return response.Text.Trim();
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] augmentation completion failed: " + exception.Message);
            }
            return null;
        }

        /// <summary>The text used to judge a graph node's relevance during reranking (chunk content, else name).</summary>
        private static string NodeText(GraphNode node)
        {
            return String.IsNullOrWhiteSpace(node.Content) ? (node.Name ?? String.Empty) : node.Content!;
        }

        /// <summary>Generate a cited answer from the given sources.</summary>
        /// <param name="question">The question.</param>
        /// <param name="sources">Supporting nodes.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runner">Answering model runner.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The answer text.</returns>
        public async Task<string> GenerateAnswerAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, string? subjectId = null, CancellationToken token = default)
        {
            GeneratedAnswer generated = await GenerateAnswerDetailedAsync(question, sources, tenantId, runner, subjectId, token).ConfigureAwait(false);
            return generated.Text;
        }

        /// <summary>Generate a cited answer from the given sources, returning the text plus generation metadata.</summary>
        /// <param name="question">The question.</param>
        /// <param name="sources">Supporting nodes.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runner">Answering model runner.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The generated answer with its model and duration (both null when generation failed).</returns>
        public async Task<GeneratedAnswer> GenerateAnswerDetailedAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, string? subjectId = null, CancellationToken token = default)
        {
            string? legacySystemPrompt = null;
            if (!String.IsNullOrEmpty(subjectId))
            {
                Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
                legacySystemPrompt = subject?.SystemPrompt;
            }
            string resolvedAnswerPrompt = await MergePromptAsync(tenantId, subjectId, "user.answer", legacySystemPrompt, token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(resolvedAnswerPrompt) ? "Answer using only the provided sources and cite them." : resolvedAnswerPrompt;

            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }

            string contextText = BuildSourceContext(question, sources);

            try
            {
                CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    Temperature = 0.2,
                    MaxTokens = 1024,
                    SystemPrompt = systemPrompt
                };
                ChatResponse response = await client.ChatAsync(contextText, options, token).ConfigureAwait(false);
                if (response != null && response.Success && !String.IsNullOrWhiteSpace(response.Text))
                {
                    string? model = String.IsNullOrEmpty(response.Model) ? runner.DefaultModel : response.Model;
                    return new GeneratedAnswer { Text = response.Text, Model = model, DurationMs = response.OverallRuntimeMs };
                }
                _Logging.Warn("[GroundedQueryService] answer generation failed: " + (response?.Error ?? "no response"));
            }
            catch (Exception e)
            {
                _Logging.Warn("[GroundedQueryService] answer generation error: " + e.Message);
            }

            return new GeneratedAnswer { Text = "An answer could not be generated, but the returned sources are relevant to your question." };
        }

        /// <summary>
        /// Generate a cited answer while streaming its tokens: emits each visible token chunk through
        /// <paramref name="onDelta"/> as the model produces it (model reasoning inside &lt;think&gt; blocks is
        /// stripped from the stream), and returns the full answer plus generation metadata once complete. Falls
        /// back to a single non-streamed answer if the model or provider does not stream successfully.
        /// </summary>
        /// <param name="question">The question.</param>
        /// <param name="sources">Supporting nodes.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runner">Answering model runner.</param>
        /// <param name="subjectId">Subject in scope, or null.</param>
        /// <param name="onDelta">Invoked with each visible token chunk as it streams.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The generated answer with its model and duration.</returns>
        public async Task<GeneratedAnswer> GenerateAnswerStreamAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, string? subjectId, Func<string, CancellationToken, Task> onDelta, CancellationToken token = default)
        {
            string? legacySystemPrompt = null;
            if (!String.IsNullOrEmpty(subjectId))
            {
                Subject? subject = await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
                legacySystemPrompt = subject?.SystemPrompt;
            }
            string resolvedAnswerPrompt = await MergePromptAsync(tenantId, subjectId, "user.answer", legacySystemPrompt, token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(resolvedAnswerPrompt) ? "Answer using only the provided sources and cite them." : resolvedAnswerPrompt;

            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }

            string contextText = BuildSourceContext(question, sources);

            try
            {
                CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
                ToolChatRequest request = new ToolChatRequest
                {
                    Messages = new List<ChatMessage> { ChatMessage.System(systemPrompt), ChatMessage.User(contextText) },
                    Tools = new List<ToolDefinition>(),
                    ToolChoice = "none",
                    Temperature = 0.2,
                    MaxTokens = 1024
                };

                ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
                if (response == null || !response.Success)
                {
                    return await StreamFallbackAsync(question, sources, tenantId, runner, subjectId, onDelta, token).ConfigureAwait(false);
                }

                // Emit each token chunk as it arrives. Consistent with the non-streaming answer path, the answer
                // text is taken verbatim from the model (no reasoning-block stripping here — that is a chat-UI
                // concern handled by the agentic path).
                StringBuilder streamed = new StringBuilder();
                await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
                {
                    if (String.IsNullOrEmpty(chunk.Text)) continue;
                    streamed.Append(chunk.Text);
                    await onDelta(chunk.Text, token).ConfigureAwait(false);
                }

                string text = (response.Text ?? String.Empty).Trim();
                if (String.IsNullOrEmpty(text)) text = streamed.ToString().Trim();
                string? model = String.IsNullOrEmpty(response.Model) ? runner.DefaultModel : response.Model;
                return new GeneratedAnswer { Text = text, Model = model, DurationMs = response.OverallRuntimeMs };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[GroundedQueryService] streaming answer error: " + e.Message);
                return await StreamFallbackAsync(question, sources, tenantId, runner, subjectId, onDelta, token).ConfigureAwait(false);
            }
        }

        /// <summary>Non-streaming fallback for <see cref="GenerateAnswerStreamAsync"/>: generate the full answer and emit it as one delta.</summary>
        private async Task<GeneratedAnswer> StreamFallbackAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, string? subjectId, Func<string, CancellationToken, Task> onDelta, CancellationToken token)
        {
            GeneratedAnswer fallback = await GenerateAnswerDetailedAsync(question, sources, tenantId, runner, subjectId, token).ConfigureAwait(false);
            if (!String.IsNullOrEmpty(fallback.Text)) await onDelta(fallback.Text, token).ConfigureAwait(false);
            return fallback;
        }

        /// <summary>
        /// Build the grounded-answer user message: the question followed by each retrieved source as a bare
        /// numbered excerpt. The node's internal type (e.g. "Cell"/"Source"/"Chunk") and raw name/id are
        /// deliberately omitted so the model has no internal plumbing to echo back to the reader (e.g. it can no
        /// longer produce "(source: Cell)"); sources are referenced by their bracketed number or a short quotation.
        /// </summary>
        /// <param name="question">The (possibly rewritten) question being answered.</param>
        /// <param name="sources">The retrieved source nodes, in presentation order.</param>
        /// <returns>The assembled context string passed to the completion model.</returns>
        public static string BuildSourceContext(string question, IEnumerable<GraphNode> sources)
        {
            StringBuilder context = new StringBuilder();
            context.AppendLine("Question: " + question);
            context.AppendLine();
            context.AppendLine("Sources:");
            int index = 1;
            foreach (GraphNode node in sources)
            {
                string content = String.IsNullOrWhiteSpace(node.Content) ? node.Name : node.Content!;
                context.AppendLine("[" + index + "] " + content);
                index++;
            }
            return context.ToString();
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Merge a subject's default retrieval filter (serialized JSON) with a per-request filter. The result is
        /// the union of both filters' required and excluded conditions, so a per-request filter narrows — never
        /// widens — the subject default. Invalid or absent JSON contributes nothing.
        /// </summary>
        /// <param name="subjectFilterJson">The subject's default filter as serialized JSON, or null.</param>
        /// <param name="requestFilter">The per-request filter, or null.</param>
        /// <returns>The merged effective filter (never null).</returns>
        private static void Absorb(RetrievalFilter into, RetrievalFilter from)
        {
            if (from.RequiredLabels != null) into.RequiredLabels.AddRange(from.RequiredLabels);
            if (from.ExcludedLabels != null) into.ExcludedLabels.AddRange(from.ExcludedLabels);
            if (from.RequiredTags != null) into.RequiredTags.AddRange(from.RequiredTags);
            if (from.ExcludedTags != null) into.ExcludedTags.AddRange(from.ExcludedTags);
        }

        private static RetrievalFilter MergeFilters(string? subjectFilterJson, RetrievalFilter? requestFilter)
        {
            RetrievalFilter merged = new RetrievalFilter();
            if (!String.IsNullOrWhiteSpace(subjectFilterJson))
            {
                RetrievalFilter? subjectFilter = null;
                try { subjectFilter = Json.Deserialize<RetrievalFilter>(subjectFilterJson!); }
                catch (Exception) { subjectFilter = null; }
                if (subjectFilter != null) Absorb(merged, subjectFilter);
            }
            if (requestFilter != null) Absorb(merged, requestFilter);
            return merged;
        }

        /// <summary>
        /// Ensure a retrieved source carries its content from the retrieval store. RecallDB is the authority
        /// for chunk text, so its content is preferred over any denormalized copy on the graph node (the graph
        /// carries structure, relationships, and provenance; RecallDB carries content). A minimal node is
        /// synthesized when the graph read returned nothing so the material is never lost.
        /// </summary>
        /// <summary>
        /// Order retrieved chunks for coherent reconstruction: group by source document (the chunk node's
        /// <c>sourceId</c> tag), order groups by their most relevant hit, and within each group present chunks
        /// in stored position order. Nodes with no source grouping fall back to their own id (each its own group).
        /// </summary>
        private static List<GraphNode> OrderForReconstruction(List<GraphNode> nodes, Dictionary<string, int> positionByNode, Dictionary<string, double> scoreByNode)
        {
            Dictionary<string, double> groupBestScore = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (GraphNode node in nodes)
            {
                string group = GroupKey(node);
                double score = scoreByNode.TryGetValue(node.Id, out double s) ? s : 0.0;
                if (!groupBestScore.TryGetValue(group, out double best) || score > best) groupBestScore[group] = score;
            }

            List<GraphNode> ordered = new List<GraphNode>(nodes);
            ordered.Sort((GraphNode a, GraphNode b) =>
            {
                string ga = GroupKey(a);
                string gb = GroupKey(b);
                if (!String.Equals(ga, gb, StringComparison.Ordinal))
                {
                    int byScore = groupBestScore[gb].CompareTo(groupBestScore[ga]);
                    if (byScore != 0) return byScore;
                    return String.CompareOrdinal(ga, gb);
                }
                int pa = positionByNode.TryGetValue(a.Id, out int va) ? va : 0;
                int pb = positionByNode.TryGetValue(b.Id, out int vb) ? vb : 0;
                return pa.CompareTo(pb);
            });
            return ordered;
        }

        /// <summary>
        /// Add a channel's Reciprocal-Rank Fusion contribution for a node: <c>weight / (k + rank)</c>, summed
        /// across the channels the node appears in. Rank is 1-based within a channel (best hit = rank 1).
        /// </summary>
        private static void AccumulateRrf(Dictionary<string, double> rrfByNode, string nodeId, double weight, int k, int rank)
        {
            double contribution = weight / (k + rank);
            rrfByNode[nodeId] = (rrfByNode.TryGetValue(nodeId, out double existing) ? existing : 0.0) + contribution;
        }

        /// <summary>Record the best (highest) raw channel score seen for a node (used only to break RRF ties).</summary>
        private static void RecordBestRaw(Dictionary<string, double> bestRawByNode, string nodeId, double score)
        {
            if (!bestRawByNode.TryGetValue(nodeId, out double existing) || score > existing) bestRawByNode[nodeId] = score;
        }

        /// <summary>
        /// Run the selected retrieval channels over the resolved collection and fuse them with Reciprocal-Rank
        /// Fusion, returning a pool of the resolved nodes with their fused/raw scores, positions, provenance, and
        /// an RRF-ordered id list. Both the grounded path and the search endpoint build on this so retrieval
        /// behaves identically across them.
        /// </summary>
        private async Task<RetrievalPool> GatherAsync(string tenantId, string question, int max, string? subjectId, RetrievalModeEnum mode, RetrievalFilter? requestFilter, string? collectionOverride, bool resolveNodes, RetrievalKnobs knobs, CancellationToken token)
        {
            RetrievalPool pool = new RetrievalPool();

            // A subject owns its retrieval configuration: its collection is where its chunks live, and its
            // embedding model is used to embed the query so it matches the stored vectors. An explicit
            // collection override (a per-request query param) wins; otherwise fall back to the subject's
            // collection, then the tenant default.
            Subject? subject = String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);
            string? collectionId = !String.IsNullOrWhiteSpace(collectionOverride)
                ? await CollectionResolver.ResolveAsync(_Collections, tenantId, collectionOverride, _Retrieval.DefaultCollectionId, token).ConfigureAwait(false)
                : (!String.IsNullOrWhiteSpace(subject?.Collection)
                    ? subject!.Collection
                    : await CollectionResolver.ResolveAsync(_Collections, tenantId, null, _Retrieval.DefaultCollectionId, token).ConfigureAwait(false));

            // All graph reads for this request go to the tenant's own LiteGraph tenant/graph.
            pool.Graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(collectionId)) return pool;

            // Without a subject there is no embedding model to embed the query with, and the vector channel used to
            // be skipped silently (so tenant-wide "hybrid" was really full-text). Use the embedding model of the
            // subjects that write to the collection being searched, when they agree on one.
            string? embeddingEndpointId = subject?.EmbeddingModel;
            if (String.IsNullOrEmpty(embeddingEndpointId) && mode != RetrievalModeEnum.FullText)
            {
                embeddingEndpointId = await ResolveCollectionEmbeddingModelAsync(tenantId, collectionId!, token).ConfigureAwait(false);
            }

            int rrfK = knobs.RrfK;

            // Facet filter: the subject's default retrieval filter merged with any per-request filter (union of
            // required, union of excluded — a request narrows, never widens, the subject default). Pushed down
            // to RecallDB's tag filter so only eligible chunks are considered.
            RetrievalFilter effectiveFilter = MergeFilters(subject?.RetrievalFilterJson, requestFilter);
            List<RetrievalTagCondition> requiredConditions = effectiveFilter.EffectiveRequired();
            List<RetrievalTagCondition> excludedConditions = effectiveFilter.EffectiveExcluded();
            IReadOnlyList<RetrievalTagCondition>? requiredFacets = requiredConditions.Count > 0 ? requiredConditions : null;
            IReadOnlyList<RetrievalTagCondition>? excludedFacets = excludedConditions.Count > 0 ? excludedConditions : null;

            IReadOnlyDictionary<string, string>? tagFilter = String.IsNullOrEmpty(subjectId)
                ? null
                : new Dictionary<string, string> { { "subjectId", subjectId } };

            IGraphRepository graph = pool.Graph;

            // Lexical (full-text) channel — used unless the mode is Vector-only.
            if (mode != RetrievalModeEnum.Vector && _Retrieval.UseInvertedIndex)
            {
                Stopwatch leg = Stopwatch.StartNew();
                try
                {
                    List<SearchHit> hits = await _Search.SearchAsync(tenantId, collectionId, question, max, tagFilter, requiredFacets, excludedFacets, token).ConfigureAwait(false);
                    pool.FusedMax += knobs.LexicalWeight / (rrfK + 1.0);
                    int rank = 0;
                    HashSet<string> channelSeen = new HashSet<string>(StringComparer.Ordinal);
                    foreach (SearchHit hit in hits)
                    {
                        if (!hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || String.IsNullOrEmpty(nodeId)) continue;

                        if (!pool.NodeById.TryGetValue(nodeId, out GraphNode? node))
                        {
                            // Resolving the full graph node is a per-hit LiteGraph round-trip; skip it when the caller
                            // does not need the node (e.g. subject search, which only groups by link/snippet/score).
                            // HydrateFromHit then synthesizes a lightweight node from the hit's content.
                            GraphNode? resolved = resolveNodes ? await graph.ReadNodeAsync(nodeId, token).ConfigureAwait(false) : null;
                            node = HydrateFromHit(resolved, nodeId, hit.Snippet);
                            if (node == null) continue;
                            if (hit.Tags.TryGetValue("linkId", out string? linkId) && !String.IsNullOrEmpty(linkId) && String.IsNullOrEmpty(node.CanonicalName))
                            {
                                node.CanonicalName = linkId;
                            }
                            pool.NodeById[nodeId] = node;
                            pool.PositionByNode[nodeId] = hit.Position;
                            if (!String.IsNullOrEmpty(hit.Snippet)) pool.SnippetByNode[nodeId] = hit.Snippet!;
                            if (!String.IsNullOrEmpty(hit.DocumentId)) pool.DocIdByNode[nodeId] = hit.DocumentId;
                            if (hit.Tags.TryGetValue("linkId", out string? lnk) && !String.IsNullOrEmpty(lnk)) pool.LinkByNode[nodeId] = lnk!;
                        }

                        if (channelSeen.Add(nodeId))
                        {
                            rank++;
                            AccumulateRrf(pool.RrfByNode, nodeId, knobs.LexicalWeight, rrfK, rank);
                            pool.TextRankByNode[nodeId] = rank;
                            pool.TextScoreByNode[nodeId] = hit.Score;
                            if (!pool.ChunkKindByNode.ContainsKey(nodeId) && hit.Tags.TryGetValue("chunkKind", out string? textKind) && !String.IsNullOrEmpty(textKind)) pool.ChunkKindByNode[nodeId] = textKind!;
                        }
                        RecordBestRaw(pool.BestRawByNode, nodeId, hit.Score);
                    }
                }
                catch (Exception exception)
                {
                    PneumaMetrics.RecordRetrievalLegFailure("text");
                    pool.DegradedLegs.Add("text");
                    _Logging.Warn("[GroundedQueryService] full-text retrieval failed: " + exception.Message);
                }

                PneumaMetrics.RecordRetrievalStage("text_leg", leg.Elapsed.TotalSeconds);
            }

            // Semantic (vector) channel — used unless the mode is FullText-only.
            if (mode != RetrievalModeEnum.FullText)
            {
                Stopwatch leg = Stopwatch.StartNew();
                try
                {
                    List<float>? queryEmbedding = await EmbedQueryAsync(question, embeddingEndpointId, token).ConfigureAwait(false);
                    PneumaMetrics.RecordRetrievalStage("embed", leg.Elapsed.TotalSeconds);
                    leg.Restart();
                    if (queryEmbedding == null || queryEmbedding.Count == 0)
                    {
                        // The query could not be embedded (no model resolved, or the model failed): the vector
                        // channel is skipped and the result is lexical only. Counted so it is never silent.
                        PneumaMetrics.RecordRetrievalLegFailure("vector");
                        pool.DegradedLegs.Add("vector");
                    }
                    else
                    {
                        List<VectorSearchHit> vectorHits = await _Vectors.SearchAsync(tenantId, collectionId, queryEmbedding, max, _Retrieval.VectorMinimumScore, tagFilter, requiredFacets, excludedFacets, token).ConfigureAwait(false);
                        pool.FusedMax += knobs.SemanticWeight / (rrfK + 1.0);
                        int rank = 0;
                        HashSet<string> channelSeen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (VectorSearchHit hit in vectorHits)
                        {
                            if (String.IsNullOrEmpty(hit.NodeId)) continue;
                            if (!pool.NodeById.TryGetValue(hit.NodeId, out GraphNode? node))
                            {
                                // Prefer a node the vector store already returned; otherwise resolve from LiteGraph
                                // only when the caller needs full nodes. When skipping, HydrateFromHit builds a stub.
                                GraphNode? resolved = resolveNodes ? (hit.Node ?? await graph.ReadNodeAsync(hit.NodeId, token).ConfigureAwait(false)) : hit.Node;
                                node = HydrateFromHit(resolved, hit.NodeId, hit.Content);
                                if (node == null) continue;
                                pool.NodeById[hit.NodeId] = node;
                                pool.PositionByNode[hit.NodeId] = hit.Position;
                                if (!String.IsNullOrEmpty(hit.Content)) pool.SnippetByNode[hit.NodeId] = hit.Content!;
                                if (!String.IsNullOrEmpty(hit.LinkId)) pool.LinkByNode[hit.NodeId] = hit.LinkId!;
                            }
                            else if (!String.IsNullOrWhiteSpace(hit.Content))
                            {
                                // Same chunk in both channels: prefer the vector hit's full chunk content.
                                node.Content = hit.Content;
                            }

                            if (channelSeen.Add(hit.NodeId))
                            {
                                rank++;
                                AccumulateRrf(pool.RrfByNode, hit.NodeId, knobs.SemanticWeight, rrfK, rank);
                                pool.VectorRankByNode[hit.NodeId] = rank;
                                pool.VectorScoreByNode[hit.NodeId] = hit.Score;
                                if (!String.IsNullOrEmpty(hit.ChunkKind)) pool.ChunkKindByNode[hit.NodeId] = hit.ChunkKind!;
                            }
                            RecordBestRaw(pool.BestRawByNode, hit.NodeId, hit.Score);
                        }
                    }
                }
                catch (Exception exception)
                {
                    PneumaMetrics.RecordRetrievalLegFailure("vector");
                    pool.DegradedLegs.Add("vector");
                    _Logging.Warn("[GroundedQueryService] vector retrieval failed: " + exception.Message);
                }

                PneumaMetrics.RecordRetrievalStage("vector_leg", leg.Elapsed.TotalSeconds);
            }

            // Order every retrieved node by its fused RRF score (tie-break by best raw channel score, then id).
            List<string> ordered = new List<string>(pool.NodeById.Keys);
            ordered.Sort((string a, string b) =>
            {
                double ra = pool.RrfByNode.TryGetValue(a, out double x) ? x : 0.0;
                double rb = pool.RrfByNode.TryGetValue(b, out double y) ? y : 0.0;
                int byRrf = rb.CompareTo(ra);
                if (byRrf != 0) return byRrf;
                double sa = pool.BestRawByNode.TryGetValue(a, out double p) ? p : 0.0;
                double sb = pool.BestRawByNode.TryGetValue(b, out double q) ? q : 0.0;
                int byRaw = sb.CompareTo(sa);
                if (byRaw != 0) return byRaw;
                return String.CompareOrdinal(a, b);
            });
            pool.OrderedIds = ordered;
            return pool;
        }

        /// <summary>
        /// The embedding model of the subjects that ingest into a collection, when they all use the same one (a
        /// collection's vectors must come from one model to be comparable). Null when no subject writes to the
        /// collection or the subjects disagree.
        /// </summary>
        private async Task<string?> ResolveCollectionEmbeddingModelAsync(string tenantId, string collectionId, CancellationToken token)
        {
            List<Subject> subjects;
            try
            {
                subjects = await _Db.Subjects.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] could not list subjects to resolve the embedding model: " + exception.Message);
                return null;
            }

            string? model = null;
            foreach (Subject candidate in subjects)
            {
                if (!String.Equals(candidate.Collection, collectionId, StringComparison.Ordinal) || String.IsNullOrWhiteSpace(candidate.EmbeddingModel)) continue;
                if (model == null) model = candidate.EmbeddingModel;
                else if (!String.Equals(model, candidate.EmbeddingModel, StringComparison.Ordinal)) return null;
            }

            return model;
        }

        /// <summary>
        /// Select up to <paramref name="max"/> passages from the fused pool with Maximal Marginal Relevance:
        /// greedily pick the candidate that best balances relevance (its normalized fused score) against novelty
        /// (one minus its greatest lexical-cosine similarity to what is already selected). Removes near-duplicate
        /// passages that would otherwise crowd the grounding context. Similarity is computed over passage text
        /// (bag-of-words cosine), so no chunk embeddings are required.
        /// </summary>
        private static List<string> SelectWithMmr(RetrievalPool pool, int max, double lambda)
        {
            List<string> candidates = pool.OrderedIds;
            if (candidates.Count <= 1 || max <= 1) return candidates.Count > max ? candidates.GetRange(0, Math.Max(0, max)) : candidates;

            double relMax = 0.0;
            foreach (string id in candidates)
            {
                double rrf = pool.RrfByNode.TryGetValue(id, out double v) ? v : 0.0;
                if (rrf > relMax) relMax = rrf;
            }
            if (relMax <= 0.0) relMax = 1.0;

            Dictionary<string, Dictionary<string, int>> freqById = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            foreach (string id in candidates)
            {
                GraphNode node = pool.NodeById[id];
                string text = String.IsNullOrWhiteSpace(node.Content) ? (node.Name ?? String.Empty) : node.Content!;
                freqById[id] = TokenFrequencies(text);
            }

            int target = Math.Min(max, candidates.Count);
            List<string> selected = new List<string>(target);
            HashSet<string> remaining = new HashSet<string>(candidates, StringComparer.Ordinal);

            // Seed with the most relevant candidate (candidates are already RRF-ordered).
            selected.Add(candidates[0]);
            remaining.Remove(candidates[0]);

            while (selected.Count < target && remaining.Count > 0)
            {
                string? best = null;
                double bestScore = Double.NegativeInfinity;
                foreach (string id in candidates)
                {
                    if (!remaining.Contains(id)) continue;
                    double relevance = (pool.RrfByNode.TryGetValue(id, out double rrf) ? rrf : 0.0) / relMax;
                    double maxSim = 0.0;
                    foreach (string chosen in selected)
                    {
                        double sim = CosineSimilarity(freqById[id], freqById[chosen]);
                        if (sim > maxSim) maxSim = sim;
                    }
                    double mmr = (lambda * relevance) - ((1.0 - lambda) * maxSim);
                    if (mmr > bestScore)
                    {
                        bestScore = mmr;
                        best = id;
                    }
                }
                if (best == null) break;
                selected.Add(best);
                remaining.Remove(best);
            }
            return selected;
        }

        /// <summary>Tokenize into a lowercase bag-of-words frequency map (alphanumeric tokens of length &gt;= 2).</summary>
        private static Dictionary<string, int> TokenFrequencies(string text)
        {
            Dictionary<string, int> frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
            if (String.IsNullOrEmpty(text)) return frequencies;
            StringBuilder token = new StringBuilder();
            foreach (char c in text)
            {
                if (Char.IsLetterOrDigit(c))
                {
                    token.Append(Char.ToLowerInvariant(c));
                }
                else if (token.Length > 0)
                {
                    AddToken(frequencies, token);
                    token.Clear();
                }
            }
            if (token.Length > 0) AddToken(frequencies, token);
            return frequencies;
        }

        private static void AddToken(Dictionary<string, int> frequencies, StringBuilder token)
        {
            if (token.Length < 2) return;
            string word = token.ToString();
            frequencies[word] = frequencies.TryGetValue(word, out int count) ? count + 1 : 1;
        }

        /// <summary>Cosine similarity between two bag-of-words frequency maps (0 when either is empty).</summary>
        private static double CosineSimilarity(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (a.Count == 0 || b.Count == 0) return 0.0;
            Dictionary<string, int> smaller = a.Count <= b.Count ? a : b;
            Dictionary<string, int> larger = a.Count <= b.Count ? b : a;
            double dot = 0.0;
            foreach (KeyValuePair<string, int> entry in smaller)
            {
                if (larger.TryGetValue(entry.Key, out int other)) dot += (double)entry.Value * other;
            }
            if (dot == 0.0) return 0.0;
            double magA = 0.0;
            foreach (int v in a.Values) magA += (double)v * v;
            double magB = 0.0;
            foreach (int v in b.Values) magB += (double)v * v;
            double denominator = Math.Sqrt(magA) * Math.Sqrt(magB);
            return denominator > 0.0 ? dot / denominator : 0.0;
        }

        /// <summary>Record the best (highest) relevance score seen for a cited content link.</summary>
        private static void RecordCitationScore(IDictionary<string, double>? scores, string linkId, double score)
        {
            if (scores == null || String.IsNullOrEmpty(linkId)) return;
            if (!scores.TryGetValue(linkId, out double existing) || score > existing) scores[linkId] = score;
        }

        private static string GroupKey(GraphNode node)
        {
            if (node.Tags != null && node.Tags.TryGetValue("sourceId", out string? sourceId) && !String.IsNullOrEmpty(sourceId)) return sourceId;
            return node.Id;
        }

        private static GraphNode? HydrateFromHit(GraphNode? node, string nodeId, string? hitContent)
        {
            if (node == null)
            {
                if (String.IsNullOrWhiteSpace(hitContent)) return null;
                return new GraphNode { Id = nodeId, Content = hitContent };
            }

            if (!String.IsNullOrWhiteSpace(hitContent))
            {
                node.Content = hitContent;
            }

            return node;
        }

        private async Task<ModelRunner?> ResolveDefaultCompletionRunnerAsync(CancellationToken token)
        {
            List<ModelRunner> runners;
            try
            {
                runners = await _Db.ModelRunners.EnumerateAsync(null, token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] could not list completion runners: " + exception.Message);
                return null;
            }

            foreach (ModelRunner candidate in runners)
            {
                if (candidate.Active && candidate.Capabilities.Contains(ModelCapabilityEnum.Completion)) return candidate;
            }
            foreach (ModelRunner candidate in runners)
            {
                if (candidate.Active) return candidate;
            }
            return null;
        }

        private async Task<List<float>?> EmbedQueryAsync(string question, string? embeddingEndpointId, CancellationToken token)
        {
            try
            {
                SemanticProcessResult processed = await _Processor.ProcessAsync(question, false, null, embeddingEndpointId, null, token).ConfigureAwait(false);
                foreach (SemanticChunk chunk in processed.Chunks)
                {
                    if (chunk.Embeddings != null && chunk.Embeddings.Count > 0) return chunk.Embeddings;
                }
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] query embedding failed: " + exception.Message);
            }
            return null;
        }

        #endregion

        #region Nested-Types

        /// <summary>
        /// The fused output of the retrieval channels: every resolved node keyed by id, with its fused RRF score,
        /// best raw channel score, stored position, provenance (link id, document id), a display snippet, the
        /// per-tenant graph client, and an RRF-ordered id list. Consumed by both the grounded path (which then
        /// applies MMR selection and reconstruction) and the search endpoint.
        /// </summary>
        private sealed class RetrievalPool
        {
            /// <summary>Resolved graph node for each retrieved chunk, keyed by node id.</summary>
            public Dictionary<string, GraphNode> NodeById { get; } = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

            /// <summary>Stored position of each retrieved chunk, keyed by node id.</summary>
            public Dictionary<string, int> PositionByNode { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Fused Reciprocal-Rank-Fusion score for each node.</summary>
            public Dictionary<string, double> RrfByNode { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>Best raw channel score (cosine / TsRank) for each node.</summary>
            public Dictionary<string, double> BestRawByNode { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>Originating content-link id for each node, when known.</summary>
            public Dictionary<string, string> LinkByNode { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Display snippet for each node, when known.</summary>
            public Dictionary<string, string> SnippetByNode { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Retrieval-store document id for each node, when known.</summary>
            public Dictionary<string, string> DocIdByNode { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>Raw cosine similarity of each node's best-ranked vector-channel hit.</summary>
            public Dictionary<string, double> VectorScoreByNode { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>Raw TsRank of each node's best-ranked full-text-channel hit.</summary>
            public Dictionary<string, double> TextScoreByNode { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

            /// <summary>1-based vector-channel rank of each node.</summary>
            public Dictionary<string, int> VectorRankByNode { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>1-based full-text-channel rank of each node.</summary>
            public Dictionary<string, int> TextRankByNode { get; } = new Dictionary<string, int>(StringComparer.Ordinal);

            /// <summary>Chunk kind (content or summary) of each node's best-ranked chunk, when tagged.</summary>
            public Dictionary<string, string> ChunkKindByNode { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

            /// <summary>The largest achievable fused score (ranked first by every channel that ran), for normalization.</summary>
            public double FusedMax { get; set; } = 0.0;

            /// <summary>Channels that failed and were skipped.</summary>
            public List<string> DegradedLegs { get; } = new List<string>();

            /// <summary>Node ids ordered by fused RRF score, most relevant first.</summary>
            public List<string> OrderedIds { get; set; } = new List<string>();

            /// <summary>The per-tenant graph client used for the retrieval (and any neighbor expansion).</summary>
            public IGraphRepository? Graph { get; set; }
        }

        #endregion
    }
}
