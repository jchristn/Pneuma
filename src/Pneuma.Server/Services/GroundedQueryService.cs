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
        private readonly IInvertedIndex _Verbex;
        private readonly IGraphRepository _Graph;
        private readonly IVectorRepository _Vectors;
        private readonly IPartioClient _Partio;
        private readonly RetrievalSettings _Retrieval;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the grounded query service.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="verbex">Inverted index.</param>
        /// <param name="graph">Graph repository.</param>
        /// <param name="vectors">Vector repository.</param>
        /// <param name="partio">Semantic processor (query embedding).</param>
        /// <param name="retrieval">Retrieval settings.</param>
        /// <param name="cipher">Cipher for decrypting model-runner keys.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public GroundedQueryService(
            DatabaseDriverBase db,
            IInvertedIndex verbex,
            IGraphRepository graph,
            IVectorRepository vectors,
            IPartioClient partio,
            RetrievalSettings retrieval,
            Aes256Cipher cipher,
            LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (verbex == null) throw new ArgumentNullException(nameof(verbex));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (vectors == null) throw new ArgumentNullException(nameof(vectors));
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (retrieval == null) throw new ArgumentNullException(nameof(retrieval));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Verbex = verbex;
            _Graph = graph;
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
        /// <param name="token">Cancellation token.</param>
        /// <returns>The grounded answer.</returns>
        public async Task<GroundedAnswer> AnswerAsync(string tenantId, string question, int max, CancellationToken token = default)
        {
            List<GraphNode> sources = await RetrieveSourcesAsync(question, max, token).ConfigureAwait(false);
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
        /// <param name="question">The question.</param>
        /// <param name="max">Maximum primary sources.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The supporting nodes.</returns>
        public async Task<List<GraphNode>> RetrieveSourcesAsync(string question, int max, CancellationToken token = default)
        {
            List<GraphNode> sources = new List<GraphNode>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

            if (_Retrieval.UseInvertedIndex)
            {
                try
                {
                    string indexId = await _Verbex.EnsureIndexAsync(token).ConfigureAwait(false);
                    List<VerbexHit> hits = await _Verbex.SearchAsync(indexId, question, max, token).ConfigureAwait(false);
                    foreach (VerbexHit hit in hits)
                    {
                        if (!hit.Tags.TryGetValue("litegraphNodeId", out string? nodeId) || String.IsNullOrEmpty(nodeId)) continue;
                        if (!seen.Add(nodeId)) continue;
                        GraphNode? node = await _Graph.ReadNodeAsync(nodeId, token).ConfigureAwait(false);
                        if (node != null) sources.Add(node);
                    }
                }
                catch (Exception exception)
                {
                    _Logging.Warn("[GroundedQueryService] inverted-index retrieval failed: " + exception.Message);
                }
            }

            try
            {
                List<float>? queryEmbedding = await EmbedQueryAsync(question, token).ConfigureAwait(false);
                if (queryEmbedding != null && queryEmbedding.Count > 0)
                {
                    List<VectorSearchHit> vectorHits = await _Vectors.SearchAsync(queryEmbedding, max, _Retrieval.VectorMinimumScore, null, token).ConfigureAwait(false);
                    foreach (VectorSearchHit hit in vectorHits)
                    {
                        if (String.IsNullOrEmpty(hit.NodeId) || !seen.Add(hit.NodeId)) continue;
                        GraphNode? node = hit.Node ?? await _Graph.ReadNodeAsync(hit.NodeId, token).ConfigureAwait(false);
                        if (node != null) sources.Add(node);
                    }
                }
            }
            catch (Exception exception)
            {
                _Logging.Warn("[GroundedQueryService] vector retrieval failed: " + exception.Message);
            }

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
                        List<GraphNode> neighbors = await _Graph.GetNeighborsAsync(seed.Id, token).ConfigureAwait(false);
                        foreach (GraphNode neighbor in neighbors)
                        {
                            if (String.IsNullOrEmpty(neighbor.Id) || !seen.Add(neighbor.Id)) continue;
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

        /// <summary>Resolve the tenant's answering model runner, if any.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An active user-prompt runner, or null.</returns>
        public async Task<ModelRunner?> ResolveAnswerRunnerAsync(string tenantId, CancellationToken token = default)
        {
            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(tenantId, token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (!runner.Active) continue;
                if (runner.Usage == ModelRunnerUsageEnum.UserPrompt || runner.Usage == ModelRunnerUsageEnum.Both) return runner;
            }
            return null;
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
