namespace Pneuma.Core.Storage
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstraction over per-stage pipeline artifact storage in an S3-compatible object store. Each
    /// ingestion artifact (source asset, atoms, chunks, embeddings, candidate subgraph) is written to
    /// its own bucket under a key derived from the originating link id and can be read back for the
    /// artifact-view endpoints. Implementations must be safe to depend on unconditionally; when object
    /// storage is disabled a no-op implementation is supplied.
    /// </summary>
    public interface IArtifactStore
    {
        /// <summary>Create each configured bucket if it does not already exist. Idempotent.</summary>
        /// <param name="token">Cancellation token.</param>
        Task EnsureBucketsAsync(CancellationToken token = default);

        /// <summary>Persist the raw source asset for a link.</summary>
        /// <param name="linkId">Originating link id (used as the object key).</param>
        /// <param name="data">Source bytes.</param>
        /// <param name="contentType">Optional MIME content type.</param>
        /// <param name="token">Cancellation token.</param>
        Task PutSourceAsync(string linkId, byte[] data, string? contentType, CancellationToken token = default);

        /// <summary>Persist the atomized document (semantic cells) JSON for a link.</summary>
        /// <param name="linkId">Originating link id (used as the object key).</param>
        /// <param name="json">Atoms JSON.</param>
        /// <param name="token">Cancellation token.</param>
        Task PutAtomsAsync(string linkId, string json, CancellationToken token = default);

        /// <summary>Persist the chunked-document JSON (chunk text, without vectors) for a link.</summary>
        /// <param name="linkId">Originating link id (used as the object key).</param>
        /// <param name="json">Chunks JSON.</param>
        /// <param name="token">Cancellation token.</param>
        Task PutChunksAsync(string linkId, string json, CancellationToken token = default);

        /// <summary>Persist the per-chunk embedding vectors JSON for a link.</summary>
        /// <param name="linkId">Originating link id (used as the object key).</param>
        /// <param name="json">Embeddings JSON.</param>
        /// <param name="token">Cancellation token.</param>
        Task PutEmbeddingsAsync(string linkId, string json, CancellationToken token = default);

        /// <summary>Persist the candidate-subgraph JSON for a link.</summary>
        /// <param name="linkId">Originating link id (used as the object key).</param>
        /// <param name="json">Candidate-subgraph JSON.</param>
        /// <param name="token">Cancellation token.</param>
        Task PutSubgraphAsync(string linkId, string json, CancellationToken token = default);

        /// <summary>Read the stored source asset for a link, or null when absent.</summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null when the object is missing.</returns>
        Task<S3ArtifactResult?> GetSourceAsync(string linkId, CancellationToken token = default);

        /// <summary>Read the stored atoms JSON for a link, or null when absent.</summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null when the object is missing.</returns>
        Task<S3ArtifactResult?> GetAtomsAsync(string linkId, CancellationToken token = default);

        /// <summary>Read the stored chunks JSON for a link, or null when absent.</summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null when the object is missing.</returns>
        Task<S3ArtifactResult?> GetChunksAsync(string linkId, CancellationToken token = default);

        /// <summary>Read the stored embedding vectors JSON for a link, or null when absent.</summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null when the object is missing.</returns>
        Task<S3ArtifactResult?> GetEmbeddingsAsync(string linkId, CancellationToken token = default);

        /// <summary>Read the stored candidate-subgraph JSON for a link, or null when absent.</summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The artifact, or null when the object is missing.</returns>
        Task<S3ArtifactResult?> GetSubgraphAsync(string linkId, CancellationToken token = default);

        /// <summary>
        /// Delete every stored artifact (source, atoms, chunks, embeddings, candidate subgraph) for a
        /// link across all buckets. Idempotent — already-absent objects are ignored.
        /// </summary>
        /// <param name="linkId">Originating link id.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAllForLinkAsync(string linkId, CancellationToken token = default);
    }
}
