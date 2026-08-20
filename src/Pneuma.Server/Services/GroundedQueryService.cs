namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
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
        private readonly IPartioClient _Partio;
        private readonly RetrievalSettings _Retrieval;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the grounded query service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository (RecallDB).</param>
        /// <param name="partio">Semantic processor (query embedding).</param>
        /// <param name="retrieval">Retrieval settings.</param>
        /// <param name="cipher">Cipher for decrypting model-runner keys.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public GroundedQueryService(
            DatabaseDriverBase db,
            IInvertedIndex search,
            ICollectionStore collections,
            IGraphRepositoryFactory graphFactory,
            IVectorRepository vectors,
            IPartioClient partio,
            RetrievalSettings retrieval,
            Aes256Cipher cipher,
            LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (vectors == null) throw new ArgumentNullException(nameof(vectors));
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (retrieval == null) throw new ArgumentNullException(nameof(retrieval));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Search = search;
            _Collections = collections;
            _GraphFactory = graphFactory;
            _Vectors = vectors;
            _Partio = partio;
            _Retrieval = retrieval;
            _Cipher = cipher;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>Answer a grounded question end to end (non-streaming).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="question">The question.</param>
        /// <param name="max">Maximum sources to retrieve.</param>
        /// <param name="subjectId">Optional subject to scope retrieval to; null searches the whole tenant.</param>
        /// <param name="citedLinkScores">Optional sink mapping each cited content-link id to the best relevance score of its chunks.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer.</returns>
        public async Task<GroundedAnswer> AnswerAsync(string tenantId, string question, int max, string? subjectId, IDictionary<string, double>? citedLinkScores, CancellationToken token = default)
        {
            List<GraphNode> sources = await RetrieveSourcesAsync(tenantId, question, max, subjectId, citedLinkScores, token).ConfigureAwait(false);
            if (sources.Count == 0)
            {
                return new GroundedAnswer
                {
                    Answer = "The archive does not contain enough information to answer that question.",
                    Grounded = false,
                    InsufficientSupport = true
                };
            }

            ModelRunner? runner = await ResolveAnswerRunnerAsync(tenantId, token).ConfigureAwait(false);
            if (runner == null)
            {
                return new GroundedAnswer
                {
                    Answer = "No answering model is configured. The returned sources are relevant to your question.",
                    Sources = sources,
                    Grounded = true
                };
            }

            GeneratedAnswer generated = await GenerateAnswerDetailedAsync(question, sources, tenantId, runner, token).ConfigureAwait(false);
            return new GroundedAnswer
            {
                Answer = generated.Text,
                Sources = sources,
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
        public async Task<List<GraphNode>> RetrieveSourcesAsync(string tenantId, string question, int max, string? subjectId, IDictionary<string, double>? citedLinkScores, CancellationToken token = default)
        {
            List<GraphNode> primary = new List<GraphNode>();
            Dictionary<string, int> positionByNode = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<string, double> scoreByNode = new Dictionary<string, double>(StringComparer.Ordinal);
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            // Both retrieval paths (full-text and vector) operate over the same RecallDB collection; resolve it
            // once within the tenant. With no collection provisioned there is nothing to retrieve.
            string? collectionId = await CollectionResolver.ResolveAsync(_Collections, tenantId, null, _Retrieval.DefaultCollectionId, token).ConfigureAwait(false);
            if (String.IsNullOrEmpty(collectionId)) return primary;

            // When a subject is specified, restrict both retrieval paths to that subject's chunks via the
            // exact-match subjectId tag every ingested chunk carries.
            IReadOnlyDictionary<string, string>? tagFilter = String.IsNullOrEmpty(subjectId)
                ? null
                : new Dictionary<string, string> { { "subjectId", subjectId } };

            // All graph reads for this request go to the tenant's own LiteGraph tenant/graph.
            IGraphRepository graph = await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false);

            if (_Retrieval.UseInvertedIndex)
            {
                try
                {
                    List<SearchHit> hits = await _Search.SearchAsync(tenantId, collectionId, question, max, tagFilter, token).ConfigureAwait(false);
                    foreach (SearchHit hit in hits)
                    {
                        if (!hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || String.IsNullOrEmpty(nodeId)) continue;
                        if (!seen.Add(nodeId)) continue;
                        GraphNode? node = await graph.ReadNodeAsync(nodeId, token).ConfigureAwait(false);
                        // RecallDB is the content authority; hydrate the source's text (and its source-document
                        // link when the graph read returned nothing) so answers are built from the stored chunk.
                        node = HydrateFromHit(node, nodeId, hit.Snippet);
                        if (node == null) continue;
                        if (hit.Tags.TryGetValue("linkId", out string? linkId) && !String.IsNullOrEmpty(linkId))
                        {
                            if (String.IsNullOrEmpty(node.CanonicalName)) node.CanonicalName = linkId;
                            RecordCitationScore(citedLinkScores, linkId, hit.Score);
                        }
                        primary.Add(node);
                        positionByNode[nodeId] = hit.Position;
                        scoreByNode[nodeId] = hit.Score;
                    }
                }
                catch (Exception exception)
                {
                    _Logging.Warn("[GroundedQueryService] full-text retrieval failed: " + exception.Message);
                }
            }

            try
            {
                List<float>? queryEmbedding = await EmbedQueryAsync(question, token).ConfigureAwait(false);
                if (queryEmbedding != null && queryEmbedding.Count > 0)
                {
                    List<VectorSearchHit> vectorHits = await _Vectors.SearchAsync(tenantId, collectionId, queryEmbedding, max, _Retrieval.VectorMinimumScore, tagFilter, token).ConfigureAwait(false);
                    foreach (VectorSearchHit hit in vectorHits)
                    {
                        if (String.IsNullOrEmpty(hit.NodeId) || !seen.Add(hit.NodeId)) continue;
                        GraphNode? node = hit.Node ?? await graph.ReadNodeAsync(hit.NodeId, token).ConfigureAwait(false);
                        node = HydrateFromHit(node, hit.NodeId, hit.Content);
                        if (node == null) continue;
                        if (!String.IsNullOrEmpty(hit.LinkId)) RecordCitationScore(citedLinkScores, hit.LinkId!, hit.Score);
                        primary.Add(node);
                        positionByNode[hit.NodeId] = hit.Position;
                        scoreByNode[hit.NodeId] = hit.Score;
                    }
                }
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] vector retrieval failed: " + exception.Message);
            }

            // RecallDB owns ordering and reconstruction: group the retrieved chunks by their source document and
            // present each group's chunks in stored position order, with the most relevant source first. This
            // reads material back the way it was written rather than in raw hit order.
            List<GraphNode> sources = OrderForReconstruction(primary, positionByNode, scoreByNode);

            // The graph owns structure, relationships, and source/citation references: expand each retrieved
            // chunk to the entities it is connected to and to its Source node, adding that context (not more raw
            // text) to the grounding set. Enabled via NeighborExpansion settings.
            if (_Retrieval.NeighborExpansionEnabled && sources.Count > 0)
            {
                int ceiling = max + _Retrieval.NeighborExpansionMaxNodes;
                try
                {
                    List<GraphNode> seeds = new List<GraphNode>(sources);
                    foreach (GraphNode seed in seeds)
                    {
                        if (sources.Count >= ceiling) break;
                        if (String.IsNullOrEmpty(seed.Id)) continue;
                        List<GraphNode> neighbors = await graph.GetNeighborsAsync(seed.Id, token).ConfigureAwait(false);
                        foreach (GraphNode neighbor in neighbors)
                        {
                            if (String.IsNullOrEmpty(neighbor.Id) || !seen.Add(neighbor.Id)) continue;
                            // Only structural neighbors add value here: entities (relationships) and the Source
                            // (citation). Sibling chunk nodes carry no content of their own — their text is in
                            // RecallDB and would only be surfaced by a direct retrieval hit.
                            if (String.Equals(neighbor.NodeType, Ontology.NodeChunk, StringComparison.Ordinal)) continue;
                            sources.Add(neighbor);
                            if (sources.Count >= ceiling) break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    _Logging.Warn("[GroundedQueryService] neighbor expansion failed: " + exception.Message);
                }
            }

            return sources;
        }

        /// <summary>
        /// Resolve the tenant's answering model runner. Prefers an explicitly-configured Pneuma model runner
        /// marked for user prompts; when none exists, falls back to the tenant's configured Partio completion
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

            return await ResolvePartioCompletionRunnerAsync(token).ConfigureAwait(false);
        }

        /// <summary>Generate a cited answer from the given sources.</summary>
        /// <param name="question">The question.</param>
        /// <param name="sources">Supporting nodes.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runner">Answering model runner.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The answer text.</returns>
        public async Task<string> GenerateAnswerAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, CancellationToken token = default)
        {
            GeneratedAnswer generated = await GenerateAnswerDetailedAsync(question, sources, tenantId, runner, token).ConfigureAwait(false);
            return generated.Text;
        }

        /// <summary>Generate a cited answer from the given sources, returning the text plus generation metadata.</summary>
        /// <param name="question">The question.</param>
        /// <param name="sources">Supporting nodes.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="runner">Answering model runner.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The generated answer with its model and duration (both null when generation failed).</returns>
        public async Task<GeneratedAnswer> GenerateAnswerDetailedAsync(string question, List<GraphNode> sources, string tenantId, ModelRunner runner, CancellationToken token = default)
        {
            Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(tenantId, "user.answer", token).ConfigureAwait(false);
            string systemPrompt = prompt?.Content ?? "Answer using only the provided sources and cite them.";

            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }

            StringBuilder context = new StringBuilder();
            context.AppendLine("Question: " + question);
            context.AppendLine();
            context.AppendLine("Sources:");
            int index = 1;
            foreach (GraphNode node in sources)
            {
                string content = String.IsNullOrWhiteSpace(node.Content) ? node.Name : node.Content!;
                context.AppendLine("[" + index + "] (" + node.NodeType + ") " + node.Name + ": " + content);
                index++;
            }

            try
            {
                CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    Temperature = 0.2,
                    MaxTokens = 1024,
                    SystemPrompt = systemPrompt
                };
                ChatResponse response = await client.ChatAsync(context.ToString(), options, token).ConfigureAwait(false);
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

        #endregion

        #region Private-Methods

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

        private async Task<ModelRunner?> ResolvePartioCompletionRunnerAsync(CancellationToken token)
        {
            List<PartioEndpoint> completions;
            try
            {
                completions = await _Partio.ListCompletionEndpointsAsync(token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] could not list completion endpoints: " + exception.Message);
                return null;
            }

            PartioEndpoint? endpoint = null;
            foreach (PartioEndpoint candidate in completions)
            {
                if (candidate.Active) { endpoint = candidate; break; }
            }
            if (endpoint == null && completions.Count > 0) endpoint = completions[0];
            if (endpoint == null) return null;

            ModelRunner runner = new ModelRunner
            {
                Name = String.IsNullOrWhiteSpace(endpoint.Name) ? "partio-completion" : endpoint.Name!,
                Provider = MapProvider(endpoint.ApiFormat),
                BaseUrl = endpoint.Endpoint ?? String.Empty,
                DefaultModel = endpoint.Model ?? String.Empty,
                Usage = ModelRunnerUsageEnum.Both,
                Active = true,
                ContextSize = endpoint.ContextSize
            };

            // The Partio endpoint's key is plaintext; store it encrypted so the shared answer path (which
            // decrypts AuthMaterialEncrypted) can consume this transient runner exactly like a stored one.
            if (!String.IsNullOrEmpty(endpoint.ApiKey))
            {
                try { runner.AuthMaterialEncrypted = _Cipher.Encrypt(endpoint.ApiKey); }
                catch (Exception) { runner.AuthMaterialEncrypted = null; }
            }

            return runner;
        }

        private static ModelRunnerProviderEnum MapProvider(string? apiFormat)
        {
            if (String.Equals(apiFormat, "OpenAI", StringComparison.OrdinalIgnoreCase)) return ModelRunnerProviderEnum.OpenAI;
            if (String.Equals(apiFormat, "Gemini", StringComparison.OrdinalIgnoreCase)) return ModelRunnerProviderEnum.Gemini;
            return ModelRunnerProviderEnum.Ollama;
        }

        private async Task<List<float>?> EmbedQueryAsync(string question, CancellationToken token)
        {
            try
            {
                PartioProcessResult processed = await _Partio.ProcessAsync(question, false, null, null, null, token).ConfigureAwait(false);
                foreach (PartioChunk chunk in processed.Chunks)
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
    }
}
