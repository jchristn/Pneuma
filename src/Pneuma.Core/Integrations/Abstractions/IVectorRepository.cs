namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral vector store for semantic retrieval: attach an embedding to a graph node and
    /// search by cosine similarity with an optional exact-match tag filter. Intended to be backed by
    /// LiteGraph's native vector search so the graph and its embeddings live in one store; a dedicated
    /// vector database could implement the same contract.
    /// </summary>
    public interface IVectorRepository
    {
        /// <summary>Attach (or replace) the embedding for a graph node.</summary>
        /// <param name="nodeId">Graph node identifier the embedding belongs to.</param>
        /// <param name="embedding">Embedding vector.</param>
        /// <param name="tags">Optional tags stored alongside the vector for later filtering.</param>
        /// <param name="token">Cancellation token.</param>
        Task UpsertVectorAsync(string nodeId, IReadOnlyList<float> embedding, IReadOnlyDictionary<string, string>? tags = null, CancellationToken token = default);

        /// <summary>
        /// Search for the nearest vectors to a query embedding, optionally restricted to nodes carrying
        /// an exact-match tag set.
        /// </summary>
        /// <param name="embedding">Query embedding.</param>
        /// <param name="topK">Maximum number of hits to return; minimum 1.</param>
        /// <param name="minimumScore">Minimum similarity score a hit must meet to be returned.</param>
        /// <param name="tags">Optional exact-match tag filter (AND); null applies no filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching hits, highest score first.</returns>
        Task<List<VectorSearchHit>> SearchAsync(IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default);
    }
}
