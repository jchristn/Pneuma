namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Bounds concurrent requests per host, so crawlers and a burst of submitted links cannot overwhelm one site. Each
    /// host gets its own gate; a caller takes a lease and disposes it when the request finishes. Thread-safe.
    /// </summary>
    public class HostRequestLimiter
    {
        #region Public-Members

        /// <summary>Maximum concurrent requests per host. Minimum 1.</summary>
        public int MaxPerHost { get; }

        #endregion

        #region Private-Members

        private readonly ConcurrentDictionary<string, SemaphoreSlim> _Gates = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the limiter.</summary>
        /// <param name="maxPerHost">Maximum concurrent requests per host; values below 1 become 1.</param>
        public HostRequestLimiter(int maxPerHost)
        {
            MaxPerHost = Math.Max(1, maxPerHost);
        }

        #endregion

        #region Public-Methods

        /// <summary>Wait for a free slot for the URL's host and take it.</summary>
        /// <param name="url">The URL whose host is limited; an unparseable URL is limited under its whole text.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A lease that frees the slot when disposed.</returns>
        public async Task<IDisposable> AcquireAsync(string url, CancellationToken token)
        {
            string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && !String.IsNullOrEmpty(uri.Host) ? uri.Host : (url ?? String.Empty);
            SemaphoreSlim gate = _Gates.GetOrAdd(host, _ => new SemaphoreSlim(MaxPerHost, MaxPerHost));
            await gate.WaitAsync(token).ConfigureAwait(false);
            return new HostLease(gate);
        }

        #endregion
    }
}
