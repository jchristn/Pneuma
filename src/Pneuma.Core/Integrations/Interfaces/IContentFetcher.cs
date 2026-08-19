namespace Pneuma.Core.Integrations.Interfaces
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Fetches the raw bytes of a source URL for ingestion. Abstracted so ingestion can be tested
    /// without live network access.
    /// </summary>
    public interface IContentFetcher
    {
        /// <summary>Fetch the bytes at a URL.</summary>
        /// <param name="url">Source URL.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The fetched bytes.</returns>
        Task<byte[]> FetchAsync(string url, CancellationToken token = default);
    }
}
