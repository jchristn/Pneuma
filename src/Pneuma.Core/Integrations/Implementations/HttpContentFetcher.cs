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
        #region Private-Members

        private static readonly HttpClient _Http = BuildClient();

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<byte[]> FetchAsync(string url, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(url)) throw new ArgumentNullException(nameof(url));
            using (HttpResponseMessage response = await _Http.GetAsync(url, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);
            }
        }

        #endregion

        #region Private-Methods

        private static HttpClient BuildClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.Add("User-Agent", "Pneuma-Ingestion/0.1");
            return client;
        }

        #endregion
    }
}
