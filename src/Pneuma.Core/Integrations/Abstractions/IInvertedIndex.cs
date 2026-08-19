namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// Provider-neutral inverted (lexical) search index: ensure an index, add documents with tags,
    /// search with an optional exact-match tag filter, and delete documents. Backed today by Verbex.
    /// Optional — when semantic retrieval is served directly from the graph/vector store, an
    /// implementation of this interface need not be configured.
    /// </summary>
    public interface IInvertedIndex
    {
        /// <summary>Ensure the index exists and return its identifier.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Index identifier.</returns>
        Task<string> EnsureIndexAsync(CancellationToken token = default);

        /// <summary>Index a document with content and tags, returning its document id.</summary>
        /// <param name="indexId">Index identifier.</param>
        /// <param name="content">Document text.</param>
        /// <param name="tags">Tags to attach (includes the graph node id).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Index document identifier.</returns>
        Task<string> AddDocumentAsync(string indexId, string content, Dictionary<string, string> tags, CancellationToken token = default);

        /// <summary>Search the index and return scored hits carrying their tags.</summary>
        /// <param name="indexId">Index identifier.</param>
        /// <param name="query">Query text.</param>
        /// <param name="maxResults">Maximum hits.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Search hits.</returns>
        Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, CancellationToken token = default);

        /// <summary>Search the index restricted to documents matching an exact-match tag filter (e.g. a subjectId).</summary>
        /// <param name="indexId">Index identifier.</param>
        /// <param name="query">Query text.</param>
        /// <param name="maxResults">Maximum hits.</param>
        /// <param name="tags">Tags every returned document must carry (AND, exact match).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Search hits, highest score first.</returns>
        Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, Dictionary<string, string> tags, CancellationToken token = default);

        /// <summary>
        /// Delete the given documents from the index by their document ids, used to cascade-remove a
        /// link's indexed chunks. Idempotent — already-absent documents are ignored.
        /// </summary>
        /// <param name="documentIds">Document identifiers to delete.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteDocumentsAsync(IEnumerable<string> documentIds, CancellationToken token = default);
    }
}
