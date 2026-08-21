namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Interfaces;

    /// <summary>
    /// Fetches source content over HTTP(S) with a bounded timeout.
    /// </summary>
    public class HttpContentFetcher : IContentFetcher
    {
        #region Public-Members

        /// <summary>
        /// Default User-Agent presented when fetching source content. A realistic modern desktop-Chrome
        /// string so sites that gate on the User-Agent (or use bot protection) serve their full content
        /// rather than blocking or degrading an obvious crawler identity.
        /// </summary>
        public const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36";

        #endregion

        #region Private-Members

        // A single shared client (no default User-Agent header): the User-Agent is set per request so
        // fetchers configured with different agents can safely share the connection pool.
        private static readonly HttpClient _Http = BuildClient();

        private readonly string _UserAgent;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate an HTTP content fetcher.</summary>
        /// <param name="userAgent">User-Agent header to present; falls back to <see cref="DefaultUserAgent"/> when null or empty.</param>
        public HttpContentFetcher(string? userAgent = null)
        {
            _UserAgent = String.IsNullOrWhiteSpace(userAgent) ? DefaultUserAgent : userAgent!;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", _UserAgent);
                using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion

        #region Private-Methods

        private static HttpClient BuildClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            return client;
        }

        #endregion
    }
}
