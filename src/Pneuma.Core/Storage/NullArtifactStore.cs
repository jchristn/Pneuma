namespace Pneuma.Core.Storage
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// No-op artifact store used when S3-compatible object storage is disabled. Writes are discarded
    /// and reads always report the object as missing, so the ingestion pipeline and artifact-view
    /// endpoints can depend on <see cref="IArtifactStore"/> unconditionally.
    /// </summary>
    public class NullArtifactStore : IArtifactStore
    {
        #region Constructors-and-Factories

        /// <summary>Instantiate the no-op artifact store.</summary>
        public NullArtifactStore()
        {
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task EnsureBucketsAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PutSourceAsync(string linkId, byte[] data, string? contentType, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PutAtomsAsync(string linkId, string json, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PutChunksAsync(string linkId, string json, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PutEmbeddingsAsync(string linkId, string json, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task PutSubgraphAsync(string linkId, string json, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<S3ArtifactResult?> GetSourceAsync(string linkId, CancellationToken token = default)
        {
            return Task.FromResult<S3ArtifactResult?>(null);
        }

        /// <inheritdoc />
        public Task<S3ArtifactResult?> GetAtomsAsync(string linkId, CancellationToken token = default)
        {
            return Task.FromResult<S3ArtifactResult?>(null);
        }

        /// <inheritdoc />
        public Task<S3ArtifactResult?> GetChunksAsync(string linkId, CancellationToken token = default)
        {
            return Task.FromResult<S3ArtifactResult?>(null);
        }

        /// <inheritdoc />
        public Task<S3ArtifactResult?> GetEmbeddingsAsync(string linkId, CancellationToken token = default)
        {
            return Task.FromResult<S3ArtifactResult?>(null);
        }

        /// <inheritdoc />
        public Task<S3ArtifactResult?> GetSubgraphAsync(string linkId, CancellationToken token = default)
        {
            return Task.FromResult<S3ArtifactResult?>(null);
        }

        /// <inheritdoc />
        public Task DeleteAllForLinkAsync(string linkId, CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
