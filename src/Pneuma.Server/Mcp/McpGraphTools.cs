namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Services;
    using Pneuma.Server.Streaming;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP tools over the knowledge graph and grounded retrieval: bounded full-text search, single-node
    /// and neighbor fetches, and the grounded-answer tool (with an optional Server-Sent-Events streaming
    /// mode). The grounded-answer logic is the shared <see cref="GroundedQueryService"/>, so the MCP and
    /// REST answers cannot drift.
    /// </summary>
    public class McpGraphTools
    {
        #region Private-Members

        private readonly IInvertedIndex _Search;
        private readonly ICollectionStore _Collections;
        private readonly string? _DefaultCollectionId;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly GroundedQueryService _Query;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the graph tools.</summary>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="defaultCollectionId">Default collection id used when a request specifies none.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="query">Shared grounded query service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public McpGraphTools(IInvertedIndex search, ICollectionStore collections, string? defaultCollectionId, IGraphRepositoryFactory graphFactory, GroundedQueryService query)
        {
            _Search = search ?? throw new ArgumentNullException(nameof(search));
            _Collections = collections ?? throw new ArgumentNullException(nameof(collections));
            _DefaultCollectionId = defaultCollectionId;
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Query = query ?? throw new ArgumentNullException(nameof(query));
        }

        #endregion

        #region Public-Methods

        /// <summary>Full-text search the corpus, returning a bounded, ranked set of node summaries.</summary>
        /// <param name="tenantId">Tenant whose RecallDB collection is searched.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="subjectId">Optional subject to scope the search to; null searches the whole tenant.</param>
        /// <param name="citedLinkScores">Optional sink mapping each cited content-link id to the best relevance score of its hits.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The ranked results payload.</returns>
        public async Task<object> SearchAsync(string tenantId, JsonElement arguments, string? subjectId, IDictionary<string, double>? citedLinkScores, CancellationToken token)
        {
            string query = McpJsonRpc.GetStringArgument(arguments, "query");
            int max = 20;
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("max", out JsonElement maxEl) && maxEl.ValueKind == JsonValueKind.Number && maxEl.TryGetInt32(out int parsed))
            {
                max = Math.Clamp(parsed, 1, 100);
            }

            List<object> results = new List<object>();
            if (String.IsNullOrWhiteSpace(query))
            {
                return new { query = String.Empty, count = 0, results };
            }

            string? collectionId = await CollectionResolver.ResolveAsync(_Collections, tenantId, null, _DefaultCollectionId, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(collectionId))
            {
                return new { query, count = 0, results };
            }

            IReadOnlyDictionary<string, string>? tagFilter = String.IsNullOrEmpty(subjectId)
                ? null
                : new Dictionary<string, string> { { "subjectId", subjectId } };
            List<SearchHit> hits = await _Search.SearchAsync(tenantId, collectionId, query, max, tagFilter, token).ConfigureAwait(false);

            // Optional reranking: when the subject has a reranking model configured, reorder the hits by
            // relevance to the query (by snippet) before they are surfaced to the assistant.
            hits = await _Query.RerankAsync(tenantId, subjectId, query, hits, h => h.Snippet ?? String.Empty, token).ConfigureAwait(false);

            IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SearchHit hit in hits)
            {
                if (!hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || String.IsNullOrEmpty(nodeId)) continue;
                if (!seen.Add(nodeId)) continue;
                if (citedLinkScores != null && hit.Tags.TryGetValue("linkId", out string? linkId) && !String.IsNullOrEmpty(linkId))
                {
                    if (!citedLinkScores.TryGetValue(linkId, out double existing) || hit.Score > existing) citedLinkScores[linkId] = hit.Score;
                }
                GraphNode? node = await graph.ReadNodeAsync(nodeId, token).ConfigureAwait(false);
                // RecallDB is the content authority; surface its chunk text as the snippet (falling back to any
                // graph-node content) so the assistant can answer directly from search results.
                string? snippet = !String.IsNullOrWhiteSpace(hit.Snippet) ? hit.Snippet : node?.Content;
                string name = !String.IsNullOrWhiteSpace(node?.Name) ? node!.Name! : (nodeId);
                string nodeType = node?.NodeType ?? String.Empty;
                results.Add(new { id = nodeId, name, nodeType, score = hit.Score, snippet = Truncate(snippet, 1200) });
                if (results.Count >= max) break;
            }

            return new { query, count = results.Count, results };
        }

        /// <summary>Fetch a single full graph node by id.</summary>
        /// <param name="tenantId">Tenant whose graph is read.</param>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The node, or null when an error response was already sent.</returns>
        public async Task<object?> GetNodeAsync(string tenantId, HttpContextBase ctx, object? id, JsonElement arguments, CancellationToken token)
        {
            string nodeId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(nodeId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            GraphNode? node = await (await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false)).ReadNodeAsync(nodeId, token).ConfigureAwait(false);
            if (node == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Graph node not found.").ConfigureAwait(false);
                return null;
            }

            return node;
        }

        /// <summary>Fetch a node's adjacent nodes as a bounded set of summaries.</summary>
        /// <param name="tenantId">Tenant whose graph is read.</param>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The neighbors payload, or null when an error response was already sent.</returns>
        public async Task<object?> GetNeighborsAsync(string tenantId, HttpContextBase ctx, object? id, JsonElement arguments, CancellationToken token)
        {
            string nodeId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(nodeId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            List<GraphNode> neighbors = await (await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false)).GetNeighborsAsync(nodeId, token).ConfigureAwait(false);
            List<object> summaries = new List<object>();
            foreach (GraphNode neighbor in neighbors)
            {
                summaries.Add(new { id = neighbor.Id, name = neighbor.Name, nodeType = neighbor.NodeType });
                if (summaries.Count >= 100) break;
            }

            return new { nodeId, count = summaries.Count, neighbors = summaries };
        }

        /// <summary>Answer a grounded question (non-streaming).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded-answer payload, or null when an error response was already sent.</returns>
        public async Task<object?> GroundedQueryAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string question = McpJsonRpc.GetStringArgument(arguments, "question");
            if (String.IsNullOrWhiteSpace(question))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'question' is required.").ConfigureAwait(false);
                return null;
            }

            int max = ClampMax(arguments);
            string tenantId = rc.TenantId ?? String.Empty;
            GroundedAnswer answer = await _Query.AnswerAsync(tenantId, question, max, null, null, token).ConfigureAwait(false);

            List<object> sources = new List<object>();
            foreach (GraphNode source in answer.Sources)
            {
                sources.Add(new { id = source.Id, name = source.Name, nodeType = source.NodeType });
            }

            return new
            {
                answer = answer.Answer,
                grounded = answer.Grounded,
                insufficientSupport = answer.InsufficientSupport,
                sources
            };
        }

        /// <summary>
        /// Answer a grounded question over a Server-Sent-Events stream (Streamable-HTTP): metadata and
        /// delta events carry the answer as it is generated, and the final event is the JSON-RPC result.
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task StreamGroundedQueryAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string question = McpJsonRpc.GetStringArgument(arguments, "question");
            if (String.IsNullOrWhiteSpace(question))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'question' is required.").ConfigureAwait(false);
                return;
            }

            int max = ClampMax(arguments);
            string tenantId = rc.TenantId ?? String.Empty;

            SseWriter sse = new SseWriter(ctx);
            try
            {
                List<GraphNode> sources = await _Query.RetrieveSourcesAsync(tenantId, question, max, null, null, token).ConfigureAwait(false);
                List<object> sourceSummaries = new List<object>();
                foreach (GraphNode source in sources)
                {
                    sourceSummaries.Add(new { id = source.Id, name = source.Name, nodeType = source.NodeType });
                }

                await sse.SendAsync(new { type = "metadata", grounded = sources.Count > 0, sourceCount = sources.Count }, false, token).ConfigureAwait(false);

                string answer;
                bool grounded;
                bool insufficientSupport;
                string? answerModel = null;
                long? generationMs = null;

                if (sources.Count == 0)
                {
                    answer = "The archive does not contain enough information to answer that question.";
                    grounded = false;
                    insufficientSupport = true;
                }
                else
                {
                    ModelRunner? runner = await _Query.ResolveAnswerRunnerAsync(tenantId, token).ConfigureAwait(false);
                    if (runner == null)
                    {
                        answer = "No answering model is configured. The returned sources are relevant to your question.";
                        grounded = true;
                        insufficientSupport = false;
                    }
                    else
                    {
                        GeneratedAnswer generated = await _Query.GenerateAnswerDetailedAsync(question, sources, tenantId, runner, null, token).ConfigureAwait(false);
                        answer = generated.Text;
                        answerModel = generated.Model;
                        generationMs = generated.DurationMs;
                        grounded = true;
                        insufficientSupport = false;
                        foreach (string chunk in SseWriter.SplitIntoChunks(answer, 48))
                        {
                            token.ThrowIfCancellationRequested();
                            await sse.SendAsync(new { type = "delta", text = chunk }, false, token).ConfigureAwait(false);
                        }
                    }
                }

                // The final SSE event is the JSON-RPC result (Streamable-HTTP transport).
                object toolResult = new { answer, grounded, insufficientSupport, model = answerModel, generationMs, sources = sourceSummaries };
                object callResult = new { content = new[] { new { type = "text", text = Json.Serialize(toolResult) } }, isError = false };
                await sse.SendAsync(new McpSuccessResponse(id, callResult), true, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                await sse.SendAsync(new { type = "error", message = "The grounded-query stream failed." }, true, token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static int ClampMax(JsonElement arguments)
        {
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("max", out JsonElement maxEl) && maxEl.ValueKind == JsonValueKind.Number && maxEl.TryGetInt32(out int parsed))
            {
                return Math.Clamp(parsed, 1, 20);
            }
            return 10;
        }

        private static string? Truncate(string? value, int max)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "…";
        }

        #endregion
    }
}
