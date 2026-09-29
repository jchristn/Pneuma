namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;

    /// <summary>
    /// Fetches source content over HTTP(S) through a <see cref="FetchSafetyPolicy"/>: only http and https, no private or
    /// internal addresses unless allow-listed (checked at connect time, so redirects and DNS rebinding are covered), a
    /// streaming size cap, validated certificates, and a per-host concurrency limit.
    /// </summary>
    public class HttpContentFetcher : IContentFetcher, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Default User-Agent presented when fetching source content. A realistic modern desktop-Chrome
        /// string so sites that gate on the User-Agent (or use bot protection) serve their full content
        /// rather than blocking or degrading an obvious crawler identity.
        /// </summary>
        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        /// <summary>The policy every fetch goes through.</summary>
        public FetchSafetyPolicy Policy { get; }

        #endregion

        #region Private-Members

        // One client per fetcher (a singleton in the server): its handler enforces the policy on every connection, and
        // the User-Agent is set per request.
        private readonly HttpClient _Http;
        private readonly string _UserAgent;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an HTTP content fetcher.</summary>
        /// <param name="userAgent">User-Agent header to present; falls back to <see cref="DefaultUserAgent"/> when null or empty.</param>
        /// <param name="policy">Fetch-safety policy; null uses the default (secure) policy.</param>
        public HttpContentFetcher(string? userAgent = null, FetchSafetyPolicy? policy = null)
        {
            _UserAgent = String.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent!;
            Policy = policy ?? new FetchSafetyPolicy();
            _Http = new HttpClient(Policy.CreateHandler(), disposeHandler: true) { Timeout = TimeSpan.FromMinutes(5) };
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="url"/> is null or empty.</exception>
        /// <exception cref="FetchBlockedException">Thrown when the policy refuses the URL or the address it resolves to.</exception>
        /// <exception cref="ContentTooLargeException">Thrown when the response exceeds the download limit.</exception>
        /// <exception cref="HttpRequestException">Thrown when the source returns a non-success status.</exception>
        public async Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            Policy.EnsureUrlShape(url);

            using (IDisposable lease = await Policy.HostLimiter.AcquireAsync(url, token).ConfigureAwait(false))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", _UserAgent);
                try
                {
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        return await Policy.ReadBodyAsync(response, url, token).ConfigureAwait(false);
                    }
                }
                catch (HttpRequestException e) when (e.InnerException is FetchBlockedException blocked)
                {
                    // The connect-time check runs inside the handler, which wraps what it throws; surface the refusal
                    // itself so the job is categorized as Blocked and not retried.
                    throw blocked;
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>Dispose resources.</summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Http.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
