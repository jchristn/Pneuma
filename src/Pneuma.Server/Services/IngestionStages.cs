namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
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
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// The work each ingestion stage performs, factored out of the orchestrating processor: ontology
    /// classification into a candidate subgraph, graph merge, embedding, search indexing (with chunk nodes
    /// and vectors), per-stage artifact persistence, and prompt-provenance recording. The processor wraps
    /// each of these in stage timing, telemetry, and event logging; this class holds only the "what."
    /// </summary>
    public class IngestionStages
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly IPartioClient _Partio;
        private readonly IInvertedIndex _Verbex;
        private readonly IGraphRepository _Graph;
        private readonly IVectorRepository _Vectors;
        private readonly IArtifactStore _Artifacts;
        private readonly PolyPromptClassifier _Classifier;
        private readonly SubgraphMerger _Merger;
        private readonly IngestionJournal _Journal;
        private readonly LoggingModule _Logging;
        private readonly bool _UseInvertedIndex;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the pipeline stages.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="partio">Partio client.</param>
        /// <param name="verbex">Verbex client.</param>
        /// <param name="graph">LiteGraph client.</param>
        /// <param name="vectors">Vector repository.</param>
        /// <param name="artifacts">Per-stage S3 artifact store.</param>
        /// <param name="journal">Journal for stage events and best-effort artifact writes.</param>
        /// <param name="useInvertedIndex">Whether the lexical inverted index is enabled.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public IngestionStages(
            DatabaseDriverBase db,
            IPartioClient partio,
            IInvertedIndex verbex,
            IGraphRepository graph,
            IVectorRepository vectors,
            IArtifactStore artifacts,
            IngestionJournal journal,
            bool useInvertedIndex,
            LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Partio = partio ?? throw new ArgumentNullException(nameof(partio));
            _Verbex = verbex ?? throw new ArgumentNullException(nameof(verbex));
            _Graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
            _Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            _Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _UseInvertedIndex = useInvertedIndex;
            _Classifier = new PolyPromptClassifier(logging);
            _Merger = new SubgraphMerger(graph);
        }

        #endregion

        #region Public-Methods

        /// <summary>Classify extracted cells into a candidate subgraph using the link's completion endpoint.</summary>
        /// <param name="job">The job.</param>
        /// <param name="cells">Extracted semantic cells.</param>
        /// <param name="subjectName">The subject's display name for grounding.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The proposed candidate subgraph.</returns>
        /// <exception cref="InvalidOperationException">Thrown when no completion endpoint is available.</exception>
        public async Task<CandidateSubgraph> ClassifyAsync(IngestionJob job, List<ExtractedCell> cells, string subjectName, CancellationToken token)
        {
            // The classification model comes from the link's chosen Partio completion endpoint — Pneuma keeps
            // no local model state. Build a transient runner from the endpoint's model/url/apiFormat.
            PartioEndpoint? endpoint = await ResolveCompletionEndpointAsync(job, token).ConfigureAwait(false);
            if (endpoint == null) throw new InvalidOperationException("No completion model endpoint is available for classification.");

            ModelRunner runner = new ModelRunner
            {
                Name = String.IsNullOrWhiteSpace(endpoint.Name) ? "partio-completion" : endpoint.Name!,
                Provider = MapProvider(endpoint.ApiFormat),
                BaseUrl = endpoint.Endpoint ?? String.Empty,
                DefaultModel = endpoint.Model ?? String.Empty
            };
            string? apiKey = endpoint.ApiKey;

            Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(job.TenantId, "ontology.classify", token).ConfigureAwait(false);
            string systemPrompt = prompt?.Content ?? "Classify the content into the subject knowledge-graph ontology.";

            Prompt? ontology = await _Db.Prompts.ReadByKeyAsync(job.TenantId, "ontology.definition", token).ConfigureAwait(false);
            string ontologyDefinition = ontology?.Content ?? String.Empty;

            return await _Classifier.ClassifyAsync(cells, systemPrompt, ontologyDefinition, runner, apiKey, subjectName, token).ConfigureAwait(false);
        }

        /// <summary>Merge a candidate subgraph into the knowledge graph, creating and linking the source node.</summary>
        /// <param name="job">The job.</param>
        /// <param name="subgraph">The candidate subgraph to merge.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merge result (created/linked node and edge ids).</returns>
        public async Task<MergeResult> MergeAsync(IngestionJob job, CandidateSubgraph subgraph, CancellationToken token)
        {
            await _Graph.EnsureGraphAsync(token).ConfigureAwait(false);

            GraphNode source = new GraphNode
            {
                NodeType = Ontology.NodeSource,
                Name = job.SourceUrl,
                CanonicalName = job.SourceUrl,
                Labels = new List<string> { Ontology.NodeSource }
            };
            source.Tags[Ontology.TagTenantId] = job.TenantId;
            source.Tags[Ontology.TagSubjectId] = job.SubjectId;
            source.Tags[Ontology.TagNodeType] = Ontology.NodeSource;
            source.Tags[Ontology.TagAssertedByJob] = job.Id;
            GraphNode createdSource = await _Graph.CreateNodeAsync(source, token).ConfigureAwait(false);

            MergeResult merge = await _Merger.MergeAsync(subgraph, job.TenantId, job.SubjectId, createdSource.Id, job.Id, token).ConfigureAwait(false);
            if (!merge.NodeIds.Contains(createdSource.Id)) merge.NodeIds.Insert(0, createdSource.Id);
            return merge;
        }

        /// <summary>Embed each cell via Partio into chunks, recording the chunking step as its own log event.</summary>
        /// <param name="job">The job.</param>
        /// <param name="cells">Extracted semantic cells.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The produced chunks (with embeddings where available).</returns>
        public async Task<List<PartioChunk>> EmbedAsync(IngestionJob job, List<ExtractedCell> cells, CancellationToken token)
        {
            Prompt? summarizePrompt = await _Db.Prompts.ReadByKeyAsync(job.TenantId, "cell.summarize", token).ConfigureAwait(false);
            string? summarizationPrompt = summarizePrompt?.Content;

            Stopwatch chunkTimer = Stopwatch.StartNew();
            List<PartioChunk> all = new List<PartioChunk>();
            foreach (ExtractedCell cell in cells)
            {
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;

                PartioProcessResult processed = await _Partio.ProcessAsync(cell.Text, true, summarizationPrompt, job.EmbeddingEndpointId, job.CompletionEndpointId, token).ConfigureAwait(false);

                List<PartioChunk> perCell = new List<PartioChunk>();
                perCell.AddRange(processed.Chunks);
                perCell.AddRange(processed.SummaryChunks);
                if (perCell.Count == 0) perCell.Add(new PartioChunk { Text = cell.Text });

                foreach (PartioChunk chunk in perCell)
                {
                    if (!String.IsNullOrWhiteSpace(chunk.Text)) all.Add(chunk);
                }
            }
            chunkTimer.Stop();

            // Chunking is a distinct step in the log, recorded before the embedding-generation completion.
            await _Journal.RecordEventAsync(job, IngestionStageEnum.Embedding, IngestionStatusEnum.Completed,
                "Chunking complete — produced " + all.Count + " chunk(s) from " + cells.Count + " cell(s).",
                chunkTimer.Elapsed.TotalMilliseconds, token).ConfigureAwait(false);

            return all;
        }

        /// <summary>Index chunks: create a Chunk node per chunk, add it to the lexical index (when enabled), and upsert its vector.</summary>
        /// <param name="job">The job.</param>
        /// <param name="merge">The graph merge result (its first node id is the source).</param>
        /// <param name="chunks">The chunks to index.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The Verbex document ids created (empty when the lexical index is disabled).</returns>
        public async Task<List<string>> IndexAsync(IngestionJob job, MergeResult merge, List<PartioChunk> chunks, CancellationToken token)
        {
            // The inverted index is optional: when disabled, retrieval is served from the graph vector
            // store alone and the lexical index is skipped.
            string? indexId = _UseInvertedIndex ? await _Verbex.EnsureIndexAsync(token).ConfigureAwait(false) : null;
            string sourceNodeId = merge.NodeIds.Count > 0 ? merge.NodeIds[0] : String.Empty;
            List<string> docIds = new List<string>();

            foreach (PartioChunk chunk in chunks)
            {
                if (String.IsNullOrWhiteSpace(chunk.Text)) continue;

                // Each chunk becomes a first-class Chunk node linked to its source, so semantic retrieval
                // resolves to chunk-level content rather than the whole source. Falls back to the source
                // node if chunk-node creation fails.
                string chunkNodeId = await CreateChunkNodeAsync(job, sourceNodeId, chunk.Text, token).ConfigureAwait(false);
                string targetNodeId = String.IsNullOrEmpty(chunkNodeId) ? sourceNodeId : chunkNodeId;

                Dictionary<string, string> tags = new Dictionary<string, string>
                {
                    { "litegraphNodeId", targetNodeId },
                    { "tenantId", job.TenantId },
                    { "subjectId", job.SubjectId },
                    { "jobId", job.Id }
                };

                if (_UseInvertedIndex && indexId != null)
                {
                    string docId = await _Verbex.AddDocumentAsync(indexId, chunk.Text, tags, token).ConfigureAwait(false);
                    if (!String.IsNullOrEmpty(docId)) docIds.Add(docId);
                }

                // Store the chunk embedding on its chunk node so semantic (vector) retrieval works
                // alongside the lexical index. Best-effort: the vector store is complementary, so a failure
                // here must not fail ingestion (the base already records the failure metric).
                if (!String.IsNullOrEmpty(targetNodeId) && chunk.Embeddings != null && chunk.Embeddings.Count > 0)
                {
                    try
                    {
                        await _Vectors.UpsertVectorAsync(targetNodeId, chunk.Embeddings, tags, token).ConfigureAwait(false);
                    }
                    catch (Exception vectorException)
                    {
                        _Logging.Warn("[IngestionStages] vector upsert failed for node " + targetNodeId + ": " + vectorException.Message);
                    }
                }
            }

            return docIds;
        }

        /// <summary>Persist the chunk texts and embedding vectors as per-link artifacts (best-effort).</summary>
        /// <param name="job">The job.</param>
        /// <param name="chunks">The chunks to persist.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task PersistChunkArtifactsAsync(IngestionJob job, List<PartioChunk> chunks, CancellationToken token)
        {
            List<string> texts = new List<string>();
            List<List<float>> vectors = new List<List<float>>();
            foreach (PartioChunk chunk in chunks)
            {
                texts.Add(chunk.Text);
                vectors.Add(chunk.Embeddings ?? new List<float>());
            }

            await _Journal.TryStoreAsync("chunks", () => _Artifacts.PutChunksAsync(job.LinkId, Json.Serialize(texts), token), token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("embeddings", () => _Artifacts.PutEmbeddingsAsync(job.LinkId, Json.Serialize(vectors), token), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Record prompt provenance so a run is reproducible: which prompt version and exact content (by
        /// hash) shaped this candidate plan. If a prompt is later edited its hash changes, and a past job's
        /// provenance still shows what it actually used.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task RecordPromptProvenanceAsync(IngestionJob job, CancellationToken token)
        {
            string[] keys = { "ontology.classify", "ontology.definition", "cell.summarize" };
            List<string> parts = new List<string>();
            foreach (string key in keys)
            {
                Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(job.TenantId, key, token).ConfigureAwait(false);
                string hash = String.IsNullOrEmpty(prompt?.Content) ? "(none)" : ShortHash(prompt!.Content!);
                int version = prompt?.Version ?? 0;
                parts.Add(key + " v" + version + "@" + hash);
            }

            await _Journal.RecordEventAsync(job, IngestionStageEnum.Categorization, IngestionStatusEnum.Processing,
                "Prompt provenance (for reproducibility): " + String.Join(", ", parts) + ".", 0, token).ConfigureAwait(false);
        }

        /// <summary>Count how many of the given chunks carry a non-empty embedding vector.</summary>
        /// <param name="chunks">The chunks.</param>
        /// <returns>The number of embedded chunks.</returns>
        public static int CountEmbeddings(List<PartioChunk> chunks)
        {
            int count = 0;
            foreach (PartioChunk chunk in chunks)
            {
                if (chunk.Embeddings != null && chunk.Embeddings.Count > 0) count++;
            }
            return count;
        }

        #endregion

        #region Private-Methods

        private async Task<PartioEndpoint?> ResolveCompletionEndpointAsync(IngestionJob job, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(job.CompletionEndpointId))
            {
                PartioEndpoint? byId = await _Partio.ReadEndpointAsync("completion", job.CompletionEndpointId, token).ConfigureAwait(false);
                if (byId != null) return byId;
            }

            List<PartioEndpoint> completions = await _Partio.ListCompletionEndpointsAsync(token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in completions)
            {
                if (endpoint.Active) return endpoint;
            }
            return completions.Count > 0 ? completions[0] : null;
        }

        private static ModelRunnerProviderEnum MapProvider(string? apiFormat)
        {
            if (String.Equals(apiFormat, "OpenAI", StringComparison.OrdinalIgnoreCase)) return ModelRunnerProviderEnum.OpenAI;
            if (String.Equals(apiFormat, "Gemini", StringComparison.OrdinalIgnoreCase)) return ModelRunnerProviderEnum.Gemini;
            return ModelRunnerProviderEnum.Ollama;
        }

        private async Task<string> CreateChunkNodeAsync(IngestionJob job, string sourceNodeId, string text, CancellationToken token)
        {
            try
            {
                GraphNode chunkNode = new GraphNode
                {
                    NodeType = Ontology.NodeChunk,
                    Name = text.Length > 80 ? text.Substring(0, 80) : text,
                    Content = text,
                    Labels = new List<string> { Ontology.NodeChunk }
                };
                chunkNode.Tags[Ontology.TagTenantId] = job.TenantId;
                chunkNode.Tags[Ontology.TagSubjectId] = job.SubjectId;
                chunkNode.Tags[Ontology.TagNodeType] = Ontology.NodeChunk;
                chunkNode.Tags[Ontology.TagAssertedByJob] = job.Id;
                if (!String.IsNullOrEmpty(sourceNodeId)) chunkNode.Tags[Ontology.TagSourceId] = sourceNodeId;

                GraphNode created = await _Graph.CreateNodeAsync(chunkNode, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(created.Id) && !String.IsNullOrEmpty(sourceNodeId))
                {
                    GraphEdge edge = new GraphEdge
                    {
                        FromNodeId = sourceNodeId,
                        ToNodeId = created.Id,
                        EdgeType = Ontology.EdgeHasChunk
                    };
                    edge.Tags[Ontology.TagAssertedByJob] = job.Id;
                    await _Graph.CreateEdgeAsync(edge, token).ConfigureAwait(false);
                }
                return created.Id;
            }
            catch (Exception exception)
            {
                _Logging.Warn("[IngestionStages] chunk node creation failed: " + exception.Message);
                return String.Empty;
            }
        }

        private static string ShortHash(string content)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content));
                System.Text.StringBuilder builder = new System.Text.StringBuilder();
                for (int i = 0; i < 4 && i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        #endregion
    }
}
