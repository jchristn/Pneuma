namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Sends requests for named hosts to a local stub instead, prefixing the path (for example api.github.com/x becomes
    /// http://127.0.0.1:port/api/x), so a library with hard-coded hosts can be tested offline.
    /// </summary>
    public sealed class HostRewriteHandler : DelegatingHandler
    {
        #region Private-Members

        private readonly string _BaseUrl;
        private readonly Dictionary<string, string> _Prefixes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="baseUrl">Stub base URL.</param>
        /// <param name="prefixes">Host to path prefix (for example api.github.com to /api).</param>
        public HostRewriteHandler(string baseUrl, Dictionary<string, string> prefixes) : base(new HttpClientHandler())
        {
            _BaseUrl = baseUrl.TrimEnd('/');
            _Prefixes = new Dictionary<string, string>(prefixes, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? prefix;
            if (request.RequestUri != null && _Prefixes.TryGetValue(request.RequestUri.Host, out prefix))
                request.RequestUri = new Uri(_BaseUrl + prefix + request.RequestUri.PathAndQuery);
            return base.SendAsync(request, cancellationToken);
        }

        #endregion
    }
}
