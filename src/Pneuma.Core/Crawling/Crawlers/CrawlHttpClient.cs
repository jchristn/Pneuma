namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// HTTP for crawlers: every request goes through the fetch-safety policy (address checks at connect time, the
    /// download size cap, and the per-host limiter) and carries the plan's Web authentication when given. Thread-safe;
    /// one instance serves every plan.
    /// </summary>
    public class CrawlHttpClient : IDisposable
    {
        #region Public-Members

        /// <summary>The fetch-safety policy.</summary>
        public FetchSafetyPolicy Policy { get; }

        /// <summary>User-Agent sent when a plan sets none.</summary>
        public string DefaultUserAgent { get; }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="policy">Fetch-safety policy.</param>
        /// <param name="userAgent">Default User-Agent, or null for the ingestion default.</param>
        /// <param name="timeout">Request timeout, or null for 60 seconds.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public CrawlHttpClient(FetchSafetyPolicy policy, string? userAgent = null, TimeSpan? timeout = null)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
            DefaultUserAgent = String.IsNullOrWhiteSpace(userAgent) ? HttpContentFetcher.DefaultUserAgent : userAgent!;
            _Http = new HttpClient(Policy.CreateHandler(), disposeHandler: true) { Timeout = timeout ?? TimeSpan.FromSeconds(60) };
        }

        #endregion

        #region Public-Methods

        /// <summary>GET a URL. Non-success statuses throw <see cref="HttpRequestException"/> with the status code.</summary>
        /// <param name="url">Absolute http or https URL.</param>
        /// <param name="web">Web settings supplying authentication and User-Agent, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="FetchBlockedException">Thrown when the policy refuses the URL.</exception>
        /// <exception cref="ContentTooLargeException">Thrown when the body exceeds the download limit.</exception>
        /// <exception cref="HttpRequestException">Thrown for a non-success status or a transport failure.</exception>
        public async Task<CrawlHttpResponse> GetAsync(string url, WebCrawlSettings? web, CancellationToken token)
        {
            Policy.EnsureUrlShape(url);
            using (IDisposable lease = await Policy.HostLimiter.AcquireAsync(url, token).ConfigureAwait(false))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", String.IsNullOrWhiteSpace(web?.UserAgent) ? DefaultUserAgent : web!.UserAgent);
                ApplyAuthentication(request, web);
                try
                {
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("GET " + url + " returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ".", null, response.StatusCode);
                        byte[] body = await Policy.ReadBodyAsync(response, url, token).ConfigureAwait(false);
                        return new CrawlHttpResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            Bytes = body,
                            ContentType = response.Content.Headers.ContentType?.MediaType,
                            ETag = response.Headers.ETag?.Tag,
                            LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime
                        };
                    }
                }
                catch (HttpRequestException e) when (e.InnerException is FetchBlockedException blocked)
                {
                    throw blocked;
                }
            }
        }

        /// <summary>
        /// Conditional GET: sends If-None-Match and If-Modified-Since when given, and returns a 304 as a response with
        /// <see cref="CrawlHttpResponse.StatusCode"/> 304 and no body. Other non-success statuses throw.
        /// </summary>
        /// <param name="url">Absolute http or https URL.</param>
        /// <param name="etag">The ETag from the last check, or null.</param>
        /// <param name="lastModifiedUtc">The Last-Modified from the last check, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The response.</returns>
        /// <exception cref="FetchBlockedException">Thrown when the policy refuses the URL.</exception>
        /// <exception cref="ContentTooLargeException">Thrown when the body exceeds the download limit.</exception>
        /// <exception cref="HttpRequestException">Thrown for a non-success status (other than 304) or a transport failure.</exception>
        public async Task<CrawlHttpResponse> GetConditionalAsync(string url, string? etag, DateTime? lastModifiedUtc, CancellationToken token)
        {
            Policy.EnsureUrlShape(url);
            using (IDisposable lease = await Policy.HostLimiter.AcquireAsync(url, token).ConfigureAwait(false))
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.TryAddWithoutValidation("User-Agent", DefaultUserAgent);
                if (!String.IsNullOrEmpty(etag)) request.Headers.TryAddWithoutValidation("If-None-Match", etag);
                if (lastModifiedUtc != null) request.Headers.IfModifiedSince = new DateTimeOffset(DateTime.SpecifyKind(lastModifiedUtc.Value, DateTimeKind.Utc));
                try
                {
                    using (HttpResponseMessage response = await _Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                        {
                            return new CrawlHttpResponse
                            {
                                StatusCode = 304,
                                ETag = response.Headers.ETag?.Tag ?? etag,
                                LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime ?? lastModifiedUtc
                            };
                        }
                        if (!response.IsSuccessStatusCode)
                            throw new HttpRequestException("GET " + url + " returned " + (int)response.StatusCode + " " + response.ReasonPhrase + ".", null, response.StatusCode);
                        byte[] body = await Policy.ReadBodyAsync(response, url, token).ConfigureAwait(false);
                        return new CrawlHttpResponse
                        {
                            StatusCode = (int)response.StatusCode,
                            Bytes = body,
                            ContentType = response.Content.Headers.ContentType?.MediaType,
                            ETag = response.Headers.ETag?.Tag,
                            LastModifiedUtc = response.Content.Headers.LastModified?.UtcDateTime
                        };
                    }
                }
                catch (HttpRequestException e) when (e.InnerException is FetchBlockedException blocked)
                {
                    throw blocked;
                }
            }
        }

        /// <summary>Add the plan's authentication header (Basic, Bearer, or API key) to a request.</summary>
        /// <param name="request">The request.</param>
        /// <param name="web">Web settings, or null for none.</param>
        public static void ApplyAuthentication(HttpRequestMessage request, WebCrawlSettings? web)
        {
            if (request == null || web == null) return;
            switch (web.Authentication)
            {
                case "Basic":
                    string pair = (web.Username ?? String.Empty) + ":" + (web.Password ?? String.Empty);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
                    break;
                case "Bearer":
                    if (!String.IsNullOrEmpty(web.BearerToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", web.BearerToken);
                    break;
                case "ApiKey":
                    if (!String.IsNullOrWhiteSpace(web.ApiKeyHeader) && !String.IsNullOrEmpty(web.ApiKey))
                        request.Headers.TryAddWithoutValidation(web.ApiKeyHeader!.Trim(), web.ApiKey);
                    break;
            }
        }

        /// <summary>Release the HTTP client.</summary>
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
