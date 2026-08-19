namespace Pneuma.Core.Storage
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Abstraction over BLOB storage for raw ingested artifacts. The default implementation writes
    /// to local disk; a Blobject-backed implementation can be substituted in production.
    /// </summary>
    public interface IBlobStore
    {
        /// <summary>Write bytes under a key and return the key.</summary>
        /// <param name="key">Storage key.</param>
        /// <param name="data">Bytes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The storage key.</returns>
        Task<string> WriteAsync(string key, byte[] data, CancellationToken token = default);

        /// <summary>Read bytes for a key, or null if not found.</summary>
        /// <param name="key">Storage key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The bytes, or null.</returns>
        Task<byte[]?> ReadAsync(string key, CancellationToken token = default);

        /// <summary>Delete the bytes stored under a key. Idempotent — a missing key is a no-op.</summary>
        /// <param name="key">Storage key.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAsync(string key, CancellationToken token = default);
    }
}
