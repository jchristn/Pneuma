namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Caching;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;
    using SyslogLogging;

    /// <summary>
    /// The work each ingestion stage performs, factored out of the orchestrating processor: ontology
    /// classification into a candidate subgraph, graph merge (source + Cell nodes), summarization, chunking,
    /// embedding, search indexing (chunks stored only in RecallDB, pointing back at their Cell node),
    /// per-stage artifact persistence, and prompt-provenance recording. The processor wraps each of these in
    /// stage timing, telemetry, and event logging; this class holds only the "what."
    /// </summary>
    public class IngestionStages
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly ISemanticProcessor _Processor;
        private readonly Aes256Cipher _Cipher;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly IVectorRepository _Vectors;
        private readonly IArtifactStore _Artifacts;
        private readonly PolyPromptClassifier _Classifier;
        private readonly IngestionJournal _Journal;
        private readonly EmbeddingCache _EmbeddingCache;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the pipeline stages.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="processor">Semantic processor (chunk/embed/summarize).</param>
        /// <param name="cipher">Cipher used to decrypt the resolved completion runner's key.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="vectors">Vector repository (RecallDB) that stores chunk content + embeddings.</param>
        /// <param name="artifacts">Per-stage S3 artifact store.</param>
        /// <param name="journal">Journal for stage events and best-effort artifact writes.</param>
        /// <param name="embeddingCache">Bounded embedding cache so identical text is not re-embedded.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public IngestionStages(
            DatabaseDriverBase db,
            ISemanticProcessor processor,
            Aes256Cipher cipher,
            IGraphRepositoryFactory graphFactory,
            IVectorRepository vectors,
            IArtifactStore artifacts,
            IngestionJournal journal,
            EmbeddingCache embeddingCache,
            LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _Cipher = cipher ?? throw new ArgumentNullException(nameof(cipher));
            _GraphFactory = graphFactory ?? throw new ArgumentNullException(nameof(graphFactory));
            _Vectors = vectors ?? throw new ArgumentNullException(nameof(vectors));
            _Artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
            _Journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _EmbeddingCache = embeddingCache ?? throw new ArgumentNullException(nameof(embeddingCache));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Classifier = new PolyPromptClassifier(logging);
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
            // The classification model comes from the link's chosen completion runner in the model-endpoint
            // store; its key is decrypted here for the direct provider call.
            ModelRunner? runner = await ResolveCompletionEndpointAsync(job, token).ConfigureAwait(false);
            if (runner == null) throw new InvalidOperationException("No completion model endpoint is available for classification.");

            string? apiKey = null;
            if (!String.IsNullOrEmpty(runner.AuthMaterialEncrypted))
            {
                try { apiKey = _Cipher.Decrypt(runner.AuthMaterialEncrypted); }
                catch (Exception) { apiKey = null; }
            }

            // Resolve the classification and ontology prompts: global base + per-subject override (Append/Replace)
            // + the legacy per-subject ontology columns, so a subject can refine classification and its ontology
            // without losing the shared base. Global is the fallback.
            Subject? subject = await _Db.Subjects.ReadAsync(job.TenantId, job.SubjectId, token).ConfigureAwait(false);
            PromptResolver resolver = new PromptResolver(_Db);

            ResolvedPrompt classifyResolved = await resolver.ResolveAsync(job.TenantId, job.SubjectId, "ontology.classify", subject?.OntologyClassifyPrompt, token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(classifyResolved.EffectiveContent) ? "Classify the content into the subject knowledge-graph ontology." : classifyResolved.EffectiveContent;

            ResolvedPrompt ontologyResolved = await resolver.ResolveAsync(job.TenantId, job.SubjectId, "ontology.definition", subject?.OntologyDefinitionPrompt, token).ConfigureAwait(false);
            string ontologyDefinition = ontologyResolved.EffectiveContent;

            return await _Classifier.ClassifyAsync(cells, systemPrompt, ontologyDefinition, runner, apiKey, subjectName, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Merge a candidate subgraph into the knowledge graph, creating and linking the source node and a Cell
        /// node per extracted cell. Cells are the graph's unit of source content: each non-empty cell becomes a
        /// Cell node linked to the source, and the returned <see cref="MergeResult.CellNodeIds"/> is aligned
        /// one-to-one with <paramref name="cells"/> so downstream chunks (which live only in RecallDB) can carry
        /// their originating cell's node id.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="subgraph">The candidate subgraph to merge.</param>
        /// <param name="cells">The extracted cells to materialize as Cell nodes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The merge result (created/linked node and edge ids, plus per-cell node ids).</returns>
        public async Task<MergeResult> MergeAsync(IngestionJob job, CandidateSubgraph subgraph, List<ExtractedCell> cells, CancellationToken token)
        {
            IGraphRepository graph = await _GraphFactory.ForTenantAsync(job.TenantId, token).ConfigureAwait(false);
            await graph.EnsureGraphAsync(token).ConfigureAwait(false);

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
            // Operator-supplied labels/tags also ride on the source graph node for provenance (labels as graph
            // labels, tags as graph tags), guarding the ontology's own reserved tag keys.
            IngestionMetadata.ApplyUserGraphMetadata(source, job);
            GraphNode createdSource = await graph.CreateNodeAsync(source, token).ConfigureAwait(false);

            MergeResult merge = await new SubgraphMerger(graph).MergeAsync(subgraph, job.TenantId, job.SubjectId, createdSource.Id, job.Id, token).ConfigureAwait(false);
            if (!merge.NodeIds.Contains(createdSource.Id)) merge.NodeIds.Insert(0, createdSource.Id);

            // Materialize each cell as a Cell node linked to the source. The list is aligned one-to-one with the
            // input cells (empty string for a cell with no text) so summarization/chunking can look a chunk's
            // originating cell node up by index.
            List<string> cellNodeIds = new List<string>(cells != null ? cells.Count : 0);
            if (cells != null)
            {
                foreach (ExtractedCell cell in cells)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(cell.Text))
                    {
                        cellNodeIds.Add(String.Empty);
                        continue;
                    }
                    string cellNodeId = await CreateCellNodeAsync(job, graph, createdSource.Id, cell.Text, token).ConfigureAwait(false);
                    cellNodeIds.Add(cellNodeId);
                    if (!String.IsNullOrEmpty(cellNodeId)) merge.NodeIds.Add(cellNodeId);
                }
            }
            merge.CellNodeIds = cellNodeIds;
            return merge;
        }

        /// <summary>Summarize each cell (one discrete pipeline step). Returns the produced summaries.</summary>
        /// <param name="job">The job.</param>
        /// <param name="cells">Extracted semantic cells.</param>
        /// <param name="cellNodeIds">Cell node ids aligned one-to-one with <paramref name="cells"/> (from the merge stage).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The non-empty summaries produced, each paired with its originating cell node id.</returns>
        public async Task<List<CellSummary>> SummarizeCellsAsync(IngestionJob job, List<ExtractedCell> cells, List<string> cellNodeIds, CancellationToken token)
        {
            ResolvedPrompt summarizeResolved = await new PromptResolver(_Db).ResolveAsync(job.TenantId, job.SubjectId, "cell.summarize", null, token).ConfigureAwait(false);
            string? summarizationPrompt = String.IsNullOrWhiteSpace(summarizeResolved.EffectiveContent) ? null : summarizeResolved.EffectiveContent;

            List<CellSummary> summaries = new List<CellSummary>();
            for (int i = 0; i < cells.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                ExtractedCell cell = cells[i];
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;
                string summary = await _Processor.SummarizeAsync(cell.Text, summarizationPrompt, job.CompletionEndpointId, token).ConfigureAwait(false);
                if (!String.IsNullOrWhiteSpace(summary))
                {
                    summaries.Add(new CellSummary
                    {
                        CellNodeId = (cellNodeIds != null && i < cellNodeIds.Count) ? cellNodeIds[i] : String.Empty,
                        Text = summary
                    });
                }
            }
            return summaries;
        }

        /// <summary>Chunk each cell (and each summary) (one discrete pipeline step); no embeddings yet.</summary>
        /// <param name="job">The job.</param>
        /// <param name="cells">Extracted semantic cells.</param>
        /// <param name="cellNodeIds">Cell node ids aligned one-to-one with <paramref name="cells"/> (from the merge stage).</param>
        /// <param name="summaries">Summaries produced by the summarization step, each carrying its cell node id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The produced chunks (text only), each stamped with its originating cell node id.</returns>
        public async Task<List<SemanticChunk>> ChunkCellsAsync(IngestionJob job, List<ExtractedCell> cells, List<string> cellNodeIds, List<CellSummary> summaries, CancellationToken token)
        {
            List<SemanticChunk> all = new List<SemanticChunk>();

            // Resolve the subject's chunking configuration so short-form and long-form subjects can chunk
            // differently; falls back to the platform defaults when the subject is missing or unset.
            ChunkingOptions options = await ResolveChunkingOptionsAsync(job, token).ConfigureAwait(false);

            for (int i = 0; i < cells.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                ExtractedCell cell = cells[i];
                if (String.IsNullOrWhiteSpace(cell.Text)) continue;
                string cellNodeId = (cellNodeIds != null && i < cellNodeIds.Count) ? cellNodeIds[i] : String.Empty;
                List<SemanticChunk> chunks = await _Processor.ChunkAsync(cell.Text, options, token).ConfigureAwait(false);
                if (chunks.Count == 0) chunks.Add(new SemanticChunk { Text = cell.Text });
                foreach (SemanticChunk chunk in chunks)
                {
                    if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                    chunk.CellNodeId = cellNodeId;
                    all.Add(chunk);
                }
            }

            if (summaries != null)
            {
                foreach (CellSummary summary in summaries)
                {
                    token.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(summary.Text)) continue;
                    List<SemanticChunk> chunks = await _Processor.ChunkAsync(summary.Text, options, token).ConfigureAwait(false);
                    if (chunks.Count == 0) chunks.Add(new SemanticChunk { Text = summary.Text });
                    foreach (SemanticChunk chunk in chunks)
                    {
                        if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                        chunk.CellNodeId = summary.CellNodeId;
                        all.Add(chunk);
                    }
                }
            }

            return all;
        }

        /// <summary>Embed the produced chunks in bounded batches (one discrete pipeline step).</summary>
        /// <param name="job">The job.</param>
        /// <param name="chunks">The chunks to embed (mutated in place with their vectors).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chunks, now carrying their embedding vectors.</returns>
        public async Task<List<SemanticChunk>> EmbedChunksAsync(IngestionJob job, List<SemanticChunk> chunks, CancellationToken token)
        {
            if (chunks.Count == 0) return chunks;

            string? model = job.EmbeddingEndpointId;

            // Serve cache hits first (identical text embedded on a prior run or elsewhere in this job is not
            // re-embedded), and collect the indices whose text still needs embedding.
            List<int> misses = new List<int>();
            for (int i = 0; i < chunks.Count; i++)
            {
                if (_EmbeddingCache.TryGet(model, chunks[i].Text, out List<float> cached))
                {
                    chunks[i].Embeddings = cached;
                }
                else
                {
                    misses.Add(i);
                }
            }

            // Embed the misses in bounded batches so a large source does not produce one enormous provider
            // request; cache each result so a later run (or duplicate content) can skip the round-trip.
            const int batchSize = 64;
            for (int start = 0; start < misses.Count; start += batchSize)
            {
                token.ThrowIfCancellationRequested();
                int count = Math.Min(batchSize, misses.Count - start);
                List<string> texts = new List<string>(count);
                for (int i = 0; i < count; i++) texts.Add(chunks[misses[start + i]].Text);

                List<List<float>> vectors = await _Processor.EmbedAsync(texts, model, token).ConfigureAwait(false);
                for (int i = 0; i < count && i < vectors.Count; i++)
                {
                    SemanticChunk chunk = chunks[misses[start + i]];
                    chunk.Embeddings = vectors[i];
                    _EmbeddingCache.Set(model, chunk.Text, vectors[i]);
                }
            }

            return chunks;
        }

        /// <summary>
        /// Store each chunk as a RecallDB document (content + embedding + provenance tags) in the job's
        /// collection. Chunks are the retrieval store's responsibility and are <b>not</b> stored in the graph;
        /// each document's <c>litegraphNodeId</c> tag points at the chunk's originating Cell node (created in
        /// the merge stage), falling back to the Source node, so a retrieval hit still resolves to a graph node
        /// for structure and neighbor expansion. Both the vector and full-text search paths operate over these
        /// same per-chunk documents. Chunks with no text or no embedding are skipped (RecallDB requires an
        /// embedding matching the collection dimensionality).
        /// </summary>
        /// <param name="job">The job; its <see cref="IngestionJob.CollectionId"/> selects the target collection.</param>
        /// <param name="merge">The graph merge result (its first node id is the source).</param>
        /// <param name="chunks">The chunks to store.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The number of chunk documents stored in the collection.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the job has no target collection assigned.</exception>
        public async Task<int> IndexAsync(IngestionJob job, MergeResult merge, List<SemanticChunk> chunks, CancellationToken token)
        {
            if (String.IsNullOrEmpty(job.CollectionId)) throw new InvalidOperationException("Ingestion job " + job.Id + " has no target collection assigned.");

            string sourceNodeId = merge.NodeIds.Count > 0 ? merge.NodeIds[0] : String.Empty;
            List<ChunkDocument> documents = new List<ChunkDocument>();
            int position = 0;

            foreach (SemanticChunk chunk in chunks)
            {
                if (String.IsNullOrWhiteSpace(chunk.Text)) continue;
                if (chunk.Embeddings == null || chunk.Embeddings.Count == 0) continue;

                // The chunk lives only in RecallDB. Its litegraphNodeId resolves to the Cell node it was derived
                // from (created during merge), falling back to the Source node when the cell has no node.
                string targetNodeId = String.IsNullOrEmpty(chunk.CellNodeId) ? sourceNodeId : chunk.CellNodeId!;

                // Provenance tags round-trip on search hits: litegraphNodeId resolves the hit to a graph node,
                // jobId scopes cascade deletion, and linkId/tenantId/subjectId scope search filters.
                Dictionary<string, string> tags = new Dictionary<string, string>
                {
                    { "litegraphNodeId", targetNodeId },
                    { "linkId", job.LinkId },
                    { "tenantId", job.TenantId },
                    { "subjectId", job.SubjectId },
                    { "jobId", job.Id },
                    { "sourceUrl", job.SourceUrl }
                };
                if (!String.IsNullOrEmpty(job.DocumentType))
                {
                    tags["documentType"] = job.DocumentType!;
                    // The source document type is also exposed as a filterable label so retrieval can scope by it
                    // (html, pdf, …) exactly like an operator-supplied label.
                    tags[RetrievalFilter.LabelTagKeyFor(job.DocumentType!)] = job.DocumentType!;
                }

                // Operator-supplied labels and tags from the originating link, stamped onto every chunk so
                // retrieval can be scoped to them. Reserved provenance keys above are never overwritten.
                IngestionMetadata.ApplyUserMetadata(tags, job);

                documents.Add(new ChunkDocument
                {
                    DocumentKey = job.Id + "_" + position.ToString(CultureInfo.InvariantCulture),
                    DocumentId = job.LinkId,
                    Position = position,
                    Content = chunk.Text,
                    Embedding = chunk.Embeddings,
                    Tags = tags
                });
                position++;
            }

            if (documents.Count > 0)
            {
                await _Vectors.StoreChunksAsync(job.TenantId, job.CollectionId, documents, token).ConfigureAwait(false);
            }

            return documents.Count;
        }

        /// <summary>Persist the chunk texts and embedding vectors as per-link artifacts (best-effort).</summary>
        /// <param name="job">The job.</param>
        /// <param name="chunks">The chunks to persist.</param>
        /// <param name="token">Cancellation token.</param>
        public async Task PersistChunkArtifactsAsync(IngestionJob job, List<SemanticChunk> chunks, CancellationToken token)
        {
            List<string> texts = new List<string>();
            List<List<float>> vectors = new List<List<float>>();
            foreach (SemanticChunk chunk in chunks)
            {
                texts.Add(chunk.Text);
                vectors.Add(chunk.Embeddings ?? new List<float>());
            }

            await _Journal.TryStoreAsync("chunks", () => _Artifacts.PutChunksAsync(job.LinkId, Json.Serialize(texts), token), token).ConfigureAwait(false);
            await _Journal.TryStoreAsync("embeddings", () => _Artifacts.PutEmbeddingsAsync(job.LinkId, Json.Serialize(vectors), token), token).ConfigureAwait(false);
        }

        /// <summary>
        /// Build the prompt-provenance summary so a run is reproducible: which prompt version and exact
        /// content (by hash) shaped this candidate plan. If a prompt is later edited its hash changes, and a
        /// past job's provenance still shows what it actually used. The caller folds this into the
        /// Categorization completion event rather than emitting a separate row.
        /// </summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A comma-separated "key vVersion@hash" summary for each prompt that shaped the run.</returns>
        public async Task<string> BuildPromptProvenanceAsync(IngestionJob job, CancellationToken token)
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

            return String.Join(", ", parts);
        }

        /// <summary>Count how many of the given chunks carry a non-empty embedding vector.</summary>
        /// <param name="chunks">The chunks.</param>
        /// <returns>The number of embedded chunks.</returns>
        public static int CountEmbeddings(List<SemanticChunk> chunks)
        {
            int count = 0;
            foreach (SemanticChunk chunk in chunks)
            {
                if (chunk.Embeddings != null && chunk.Embeddings.Count > 0) count++;
            }
            return count;
        }

        #endregion

        #region Private-Methods

        /// <summary>Resolve the chunking configuration for a job from its subject, falling back to defaults.</summary>
        /// <param name="job">The job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The chunking options to apply.</returns>
        private async Task<ChunkingOptions> ResolveChunkingOptionsAsync(IngestionJob job, CancellationToken token)
        {
            ChunkingOptions options = new ChunkingOptions();
            Subject? subject = await _Db.Subjects.ReadAsync(job.TenantId, job.SubjectId, token).ConfigureAwait(false);
            if (subject == null) return options;
            if (!String.IsNullOrWhiteSpace(subject.ChunkStrategy)) options.Strategy = subject.ChunkStrategy!;
            options.MaxTokens = subject.ChunkMaxTokens;
            options.OverlapCount = subject.ChunkOverlapTokens;
            return options;
        }

        private async Task<ModelRunner?> ResolveCompletionEndpointAsync(IngestionJob job, CancellationToken token)
        {
            if (!String.IsNullOrWhiteSpace(job.CompletionEndpointId))
            {
                ModelRunner? byId = await _Db.ModelRunners.ReadAsync(job.CompletionEndpointId!, token).ConfigureAwait(false);
                if (byId != null && byId.Active) return byId;
            }

            List<ModelRunner> runners = await _Db.ModelRunners.EnumerateAsync(job.TenantId, token).ConfigureAwait(false);
            foreach (ModelRunner runner in runners)
            {
                if (!runner.Active) continue;
                if (runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) return runner;
            }
            return null;
        }

        private async Task<string> CreateCellNodeAsync(IngestionJob job, IGraphRepository graph, string sourceNodeId, string text, CancellationToken token)
        {
            try
            {
                // A Cell node is the graph's unit of source content: it holds the cell's extracted text and links
                // to its source. Its finer-grained chunks are not graph nodes — they live only in RecallDB and
                // point back here via litegraphNodeId.
                GraphNode cellNode = new GraphNode
                {
                    NodeType = Ontology.NodeCell,
                    Name = text.Length > 80 ? text.Substring(0, 80) : text,
                    Content = text,
                    Labels = new List<string> { Ontology.NodeCell }
                };
                cellNode.Tags[Ontology.TagTenantId] = job.TenantId;
                cellNode.Tags[Ontology.TagSubjectId] = job.SubjectId;
                cellNode.Tags[Ontology.TagNodeType] = Ontology.NodeCell;
                cellNode.Tags[Ontology.TagAssertedByJob] = job.Id;
                if (!String.IsNullOrEmpty(sourceNodeId)) cellNode.Tags[Ontology.TagSourceId] = sourceNodeId;

                GraphNode created = await graph.CreateNodeAsync(cellNode, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(created.Id) && !String.IsNullOrEmpty(sourceNodeId))
                {
                    GraphEdge edge = new GraphEdge
                    {
                        FromNodeId = sourceNodeId,
                        ToNodeId = created.Id,
                        EdgeType = Ontology.EdgeHasCell
                    };
                    edge.Tags[Ontology.TagAssertedByJob] = job.Id;
                    await graph.CreateEdgeAsync(edge, token).ConfigureAwait(false);
                }
                return created.Id;
            }
            catch (Exception exception)
            {
                _Logging.Warn("[IngestionStages] cell node creation failed: " + exception.Message);
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
