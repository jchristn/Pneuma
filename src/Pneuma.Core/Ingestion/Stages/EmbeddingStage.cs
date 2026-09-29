namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Embeds the produced chunks in bounded batches (serving cache hits first so identical text is not
    /// re-embedded) and then persists the chunk texts and embedding vectors as per-link artifacts (best-effort).
    /// A chunk is embedded with its context header when it has one. When the model rejects a chunk as longer than its
    /// context window, that chunk alone is re-chunked at 75%, 50%, and then 30% of the chunk size; a chunk the model
    /// rejects even at 30% is dropped with a warning. Folding the artifact persistence into this stage keeps it inside
    /// the concurrency gate, timeout, and telemetry span like every other unit of work.
    /// </summary>
    public class EmbeddingStage : IStage
    {
        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Embedding;

        #endregion

        #region Private-Members

        private const int _BatchSize = 64;
        private static readonly double[] _RechunkScales = new double[] { 0.75, 0.5, 0.3 };
        private readonly StageDependencies _Deps;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the stage.</summary>
        /// <param name="deps">Shared stage dependencies.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="deps"/> is null.</exception>
        public EmbeddingStage(StageDependencies deps)
        {
            _Deps = deps ?? throw new ArgumentNullException(nameof(deps));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            List<SemanticChunk> chunks = context.Chunks;
            string? model = job.EmbeddingEndpointId;

            if (chunks.Count > 0)
            {
                // Serve cache hits first (identical text embedded on a prior run or elsewhere in this job is not
                // re-embedded), and collect the indices whose text still needs embedding.
                List<int> misses = new List<int>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (_Deps.EmbeddingCache.TryGet(model, EmbeddedText(chunks[i]), out List<float> cached)) chunks[i].Embeddings = cached;
                    else misses.Add(i);
                }

                // Embed the misses in bounded batches so a large source does not produce one enormous provider
                // request; cache each result so a later run (or duplicate content) can skip the round-trip.
                List<int> tooLong = new List<int>();
                for (int start = 0; start < misses.Count; start += _BatchSize)
                {
                    token.ThrowIfCancellationRequested();
                    List<int> batch = misses.GetRange(start, Math.Min(_BatchSize, misses.Count - start));
                    await EmbedBatchAsync(chunks, batch, model, tooLong, token).ConfigureAwait(false);
                }

                if (tooLong.Count > 0) await RechunkTooLongAsync(context, tooLong, model, token).ConfigureAwait(false);
            }

            chunks = context.Chunks;
            await PersistChunkArtifactsAsync(job, chunks, token).ConfigureAwait(false);

            // A chunk without a vector can never be found by semantic search, so it is not dropped: the attempt fails
            // and is retried instead.
            int embedded = CountEmbeddings(chunks);
            job.Completeness.ChunksEmbedded = embedded;
            if (embedded < chunks.Count)
            {
                throw new InvalidOperationException("Embedding produced vectors for " + embedded.ToString(CultureInfo.InvariantCulture) + " of " +
                    chunks.Count.ToString(CultureInfo.InvariantCulture) + " chunk(s).");
            }

            context.Message = "Embedding complete: produced " + embedded + " embedding vector(s) across " + chunks.Count + " chunk(s).";
        }

        #endregion

        #region Private-Methods

        private static string EmbeddedText(SemanticChunk chunk)
        {
            return String.IsNullOrEmpty(chunk.EmbeddingText) ? chunk.Text : chunk.EmbeddingText!;
        }

        // Embed a batch of chunk indices. When the model rejects the batch as too long, embed its chunks one at a time
        // so only the chunks that are actually too long are set aside for re-chunking.
        private async Task EmbedBatchAsync(List<SemanticChunk> chunks, List<int> indices, string? model, List<int> tooLong, CancellationToken token)
        {
            List<string> texts = new List<string>(indices.Count);
            foreach (int i in indices) texts.Add(EmbeddedText(chunks[i]));

            try
            {
                List<List<float>> vectors = await _Deps.Processor.EmbedAsync(texts, model, token).ConfigureAwait(false);
                for (int k = 0; k < indices.Count && k < vectors.Count; k++) Assign(chunks[indices[k]], vectors[k], model);
            }
            catch (ModelRequestRejectedException e) when (e.IsContextLength)
            {
                if (indices.Count == 1)
                {
                    tooLong.Add(indices[0]);
                    return;
                }

                foreach (int i in indices)
                {
                    await EmbedBatchAsync(chunks, new List<int> { i }, model, tooLong, token).ConfigureAwait(false);
                }
            }
        }

        private void Assign(SemanticChunk chunk, List<float> vector, string? model)
        {
            chunk.Embeddings = vector;
            if (vector != null && vector.Count > 0) _Deps.EmbeddingCache.Set(model, EmbeddedText(chunk), vector);
        }

        // Replace each too-long chunk with smaller chunks cut from its own text, trying progressively smaller sizes.
        private async Task RechunkTooLongAsync(StageContext context, List<int> tooLong, string? model, CancellationToken token)
        {
            List<SemanticChunk> chunks = context.Chunks;
            ChunkingOptions baseOptions = context.ChunkingOptions ?? new ChunkingOptions();
            Dictionary<int, List<SemanticChunk>> replacements = new Dictionary<int, List<SemanticChunk>>();

            foreach (int index in tooLong)
            {
                SemanticChunk original = chunks[index];
                List<SemanticChunk>? pieces = null;
                foreach (double scale in _RechunkScales)
                {
                    token.ThrowIfCancellationRequested();
                    ChunkingOptions smaller = baseOptions.Clone();
                    smaller.MaxTokens = Math.Max(16, (int)(baseOptions.MaxTokens * scale));
                    smaller.OverlapCount = Math.Min(baseOptions.OverlapCount, smaller.MaxTokens / 4);
                    smaller.ContextHeader = original.Header;
                    List<SemanticChunk> candidate = await _Deps.Processor.ChunkAsync(original.Text, smaller, token).ConfigureAwait(false);
                    PneumaMetrics.RecordIngestionRechunk(scale.ToString("0.##", CultureInfo.InvariantCulture));

                    List<int> all = new List<int>();
                    for (int k = 0; k < candidate.Count; k++)
                    {
                        candidate[k].CellNodeId = original.CellNodeId;
                        candidate[k].Kind = original.Kind;
                        all.Add(k);
                    }

                    List<int> stillTooLong = new List<int>();
                    await EmbedBatchAsync(candidate, all, model, stillTooLong, token).ConfigureAwait(false);
                    if (stillTooLong.Count == 0 && candidate.Count > 0)
                    {
                        pieces = candidate;
                        break;
                    }
                }

                if (pieces == null)
                {
                    context.AddWarning("A chunk of " + original.Text.Length.ToString(CultureInfo.InvariantCulture) +
                        " characters was rejected by the embedding model as too long even at 30% of the chunk size and was left out of the index. Lower the runner's maximum input tokens or the subject's chunk size.");
                    PneumaMetrics.RecordIngestionPartial("Embedding", "context_length");
                    pieces = new List<SemanticChunk>();
                }

                replacements[index] = pieces;
            }

            List<SemanticChunk> rebuilt = new List<SemanticChunk>(chunks.Count);
            for (int i = 0; i < chunks.Count; i++)
            {
                if (replacements.TryGetValue(i, out List<SemanticChunk>? pieces)) rebuilt.AddRange(pieces);
                else rebuilt.Add(chunks[i]);
            }

            context.Chunks = rebuilt;
            context.Job.Completeness.ChunksProduced = rebuilt.Count;
        }

        private async Task PersistChunkArtifactsAsync(IngestionJob job, List<SemanticChunk> chunks, CancellationToken token)
        {
            List<string> texts = new List<string>();
            List<List<float>> vectors = new List<List<float>>();
            foreach (SemanticChunk chunk in chunks)
            {
                texts.Add(chunk.Text);
                vectors.Add(chunk.Embeddings ?? new List<float>());
            }

            await _Deps.Journal.TryStoreAsync("chunks", () => _Deps.Artifacts.PutChunksAsync(job.LinkId, Json.Serialize(texts), token), token).ConfigureAwait(false);
            await _Deps.Journal.TryStoreAsync("embeddings", () => _Deps.Artifacts.PutEmbeddingsAsync(job.LinkId, Json.Serialize(vectors), token), token).ConfigureAwait(false);
        }

        private static int CountEmbeddings(List<SemanticChunk> chunks)
        {
            int count = 0;
            foreach (SemanticChunk chunk in chunks)
            {
                if (chunk.Embeddings != null && chunk.Embeddings.Count > 0) count++;
            }

            return count;
        }

        #endregion
    }
}
