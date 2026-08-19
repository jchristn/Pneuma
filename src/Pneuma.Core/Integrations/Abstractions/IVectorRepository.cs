namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral vector store for semantic retrieval, backed by a RecallDB collection. A chunk is
    /// stored as one document carrying its content, embedding, and tags; semantic search returns the
    /// nearest chunks by vector similarity within a collection. Vectors live in the dedicated store, not
    /// in the knowledge graph; hits resolve back to graph nodes via the <c>litegraphNodeId</c> tag.
    /// </summary>
    public interface IVectorRepository
    {
        /// <summary>Store a batch of chunk documents — content plus embedding and tags — in a tenant's collection.</summary>
        /// <param name="tenantId">RecallDB tenant that owns the collection.</param>
        /// <param name="collectionId">Target collection identifier.</param>
        /// <param name="chunks">The chunk documents to store. Each embedding's length must match the collection's dimensionality.</param>
        /// <param name="token">Cancellation token.</param>
        Task StoreChunksAsync(string tenantId, string collectionId, IReadOnlyList<ChunkDocument> chunks, CancellationToken token = default);

        /// <summary>
        /// Search a tenant's collection for the nearest vectors to a query embedding, optionally restricted
        /// to chunks carrying an exact-match tag set.
        /// </summary>
        /// <param name="tenantId">RecallDB tenant that owns the collection.</param>
        /// <param name="collectionId">Collection to search.</param>
        /// <param name="embedding">Query embedding.</param>
        /// <param name="topK">Maximum number of hits to return; minimum 1.</param>
        /// <param name="minimumScore">Minimum similarity score a hit must meet to be returned.</param>
        /// <param name="tags">Optional exact-match tag filter (AND); null applies no filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching hits, highest score first.</returns>
        Task<List<VectorSearchHit>> SearchAsync(string tenantId, string collectionId, IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default);

        /// <summary>Delete every chunk in a tenant's collection matching an exact-match tag (e.g. a jobId), to cascade-remove a source's chunks. Idempotent.</summary>
        /// <param name="tenantId">RecallDB tenant that owns the collection.</param>
        /// <param name="collectionId">Collection to delete from.</param>
        /// <param name="tagKey">Tag key to match.</param>
        /// <param name="tagValue">Tag value to match.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByTagAsync(string tenantId, string collectionId, string tagKey, string tagValue, CancellationToken token = default);
    }
}
