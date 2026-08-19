namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral lexical (full-text) search over the same chunk collection the vector store writes.
    /// Backed today by RecallDB's full-text query. Documents are written once by
    /// <see cref="IVectorRepository.StoreChunkAsync"/> (content + embedding together); this interface only
    /// reads, returning scored hits that carry their tags for round-trip back to the knowledge graph.
    /// </summary>
    public interface IInvertedIndex
    {
        /// <summary>Full-text search a tenant's collection, optionally restricted to chunks matching an exact-match tag filter.</summary>
        /// <param name="tenantId">RecallDB tenant that owns the collection.</param>
        /// <param name="collectionId">Collection to search.</param>
        /// <param name="query">Query text.</param>
        /// <param name="maxResults">Maximum hits.</param>
        /// <param name="tags">Optional tags every returned chunk must carry (AND, exact match); null applies no filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Search hits, highest score first.</returns>
        Task<List<SearchHit>> SearchAsync(string tenantId, string collectionId, string query, int maxResults, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default);
    }
}
