namespace Pneuma.Core.Ingestion.Stages
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// Embeds the produced chunks in bounded batches (serving cache hits first so identical text is not
    /// re-embedded) and then persists the chunk texts and embedding vectors as per-link artifacts (best-effort).
    /// Folding the artifact persistence into this stage keeps it inside the concurrency gate, timeout, and
    /// telemetry span like every other unit of work.
    /// </summary>
    public class EmbeddingStage : IStage
    {
        #region Private-Members

        private const int _BatchSize = 64;
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

        #region Public-Members

        /// <inheritdoc />
        public IngestionStageEnum Stage => IngestionStageEnum.Embedding;

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task ExecuteAsync(StageContext context, CancellationToken token)
        {
            IngestionJob job = context.Job;
            List<SemanticChunk> chunks = context.Chunks;

            if (chunks.Count > 0)
            {
                string? model = job.EmbeddingEndpointId;

                // Serve cache hits first (identical text embedded on a prior run or elsewhere in this job is not
                // re-embedded), and collect the indices whose text still needs embedding.
                List<int> misses = new List<int>();
                for (int i = 0; i < chunks.Count; i++)
                {
                    if (_Deps.EmbeddingCache.TryGet(model, chunks[i].Text, out List<float> cached))
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
                for (int start = 0; start < misses.Count; start += _BatchSize)
                {
                    token.ThrowIfCancellationRequested();
                    int count = Math.Min(_BatchSize, misses.Count - start);
                    List<string> texts = new List<string>(count);
                    for (int i = 0; i < count; i++) texts.Add(chunks[misses[start + i]].Text);

                    List<List<float>> vectors = await _Deps.Processor.EmbedAsync(texts, model, token).ConfigureAwait(false);
                    for (int i = 0; i < count && i < vectors.Count; i++)
                    {
                        SemanticChunk chunk = chunks[misses[start + i]];
                        chunk.Embeddings = vectors[i];
                        _Deps.EmbeddingCache.Set(model, chunk.Text, vectors[i]);
                    }
                }
            }

            await PersistChunkArtifactsAsync(job, chunks, token).ConfigureAwait(false);

            context.Message = "Embedding complete — produced " + CountEmbeddings(chunks) + " embedding vector(s) across " + chunks.Count + " chunk(s).";
        }

        #endregion

        #region Private-Methods

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
