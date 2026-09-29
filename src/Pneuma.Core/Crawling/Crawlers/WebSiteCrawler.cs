namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using CrawlSharp.Web;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;
    using SyslogLogging;

    /// <summary>
    /// Crawls web sites with CrawlSharp: follows links from the start URLs within the plan's scope and depth, honors
    /// robots.txt and the crawl delay, and uses the page's SHA-256 (or its ETag or Last-Modified) as its version. Every
    /// start URL and every discovered page must pass the fetch-safety policy; content is re-read for ingestion through
    /// the same policy with the plan's authentication.
    /// </summary>
    public class WebSiteCrawler : ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.Web;

        /// <inheritdoc />
        public string DisplayName => "Web site";

        /// <inheritdoc />
        public string Description => "Follows links from start URLs within a scope and depth, honoring robots.txt. A page is re-ingested when its content changes.";

        #endregion

        #region Private-Members

        private readonly CrawlHttpClient _Http;
        private readonly LoggingModule? _Logging;
        private readonly string _Header = "[WebSiteCrawler] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="http">Policy-enforcing HTTP client.</param>
        /// <param name="logging">Logging module, or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="http"/> is null.</exception>
        public WebSiteCrawler(CrawlHttpClient http, LoggingModule? logging = null)
        {
            _Http = http ?? throw new ArgumentNullException(nameof(http));
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public List<string> CheckSettings(CrawlPlan plan)
        {
            List<string> problems = new List<string>();
            WebCrawlSettings? web = plan?.Web;
            if (web == null) return problems;
            if (web.Authentication == "Basic" && String.IsNullOrWhiteSpace(web.Username)) problems.Add("web.username is required for Basic authentication.");
            if (web.Authentication == "ApiKey" && String.IsNullOrWhiteSpace(web.ApiKeyHeader)) problems.Add("web.apiKeyHeader is required for API-key authentication.");
            foreach (string url in web.StartUrls)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                    problems.Add("web.startUrls: " + url + " is not an absolute http or https URL.");
            }
            return problems;
        }

        /// <inheritdoc />
        public async Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token)
        {
            ConnectivityResult result = new ConnectivityResult();
            WebCrawlSettings? web = plan?.Web;
            if (web == null || web.StartUrls.Count == 0)
            {
                result.Add("settings", false, "Add at least one start URL.");
                return result;
            }
            foreach (string startUrl in web.StartUrls)
            {
                Uri? uri;
                if (!Uri.TryCreate(startUrl, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    result.Add("settings", false, startUrl + " is not an absolute http or https URL.");
                    return result;
                }
            }
            result.Add("settings", true, web.StartUrls.Count + " start URL(s).");

            Uri first = new Uri(web.StartUrls[0]);
            if (!await ConnectivityProbe.ResolveAsync(result, first.Host, token).ConfigureAwait(false)) return result;
            if (!await _Http.Policy.IsAllowedAsync(first.AbsoluteUri, token).ConfigureAwait(false))
            {
                result.Add("policy", false, first.Host + " is a private or internal address. An administrator can allow it in Ingestion.FetchSafety.AllowedPrivateHosts.");
                return result;
            }
            result.Add("policy", true, "The fetch-safety policy allows " + first.Host + ".");
            if (!await ConnectivityProbe.ConnectAsync(result, first.Host, first.Port, 10000, token).ConfigureAwait(false)) return result;

            try
            {
                CrawlHttpResponse response = await _Http.GetAsync(first.AbsoluteUri, web, token).ConfigureAwait(false);
                result.Add("auth", true, web.Authentication == "None" ? "No authentication configured." : "Signed in with " + web.Authentication + " authentication.");
                result.Add("root", true, "GET " + first.AbsoluteUri + " returned " + response.StatusCode + " (" + (response.ContentType ?? "unknown type") + ", " + response.Bytes.Length + " bytes).");
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.Unauthorized || e.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                result.Add("auth", false, "The site refused the request (" + (int)e.StatusCode! + "). Check the authentication settings.");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                result.Add("root", false, "GET " + first.AbsoluteUri + " failed: " + e.Message);
            }
            return result;
        }

        /// <inheritdoc />
        /// <exception cref="FetchBlockedException">Thrown when a start URL is refused by the fetch-safety policy.</exception>
        public async IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, [EnumeratorCancellation] CancellationToken token)
        {
            WebCrawlSettings web = plan?.Web ?? throw new ArgumentException("A Web plan needs web settings.");
            if (web.StartUrls.Count == 0) throw new ArgumentException("A Web plan needs at least one start URL.");
            foreach (string startUrl in web.StartUrls) await _Http.Policy.EnsureAllowedAsync(startUrl, token).ConfigureAwait(false);

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string startUrl in web.StartUrls)
            {
                using (CancellationTokenSource stop = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (WebCrawler crawler = new WebCrawler(BuildSettings(web, startUrl), stop.Token))
                {
                    crawler.Logger = (msg) => _Logging?.Debug(_Header + msg);
                    crawler.Exception = (msg, ex) => _Logging?.Warn(_Header + msg + " " + ex.Message);
                    try
                    {
                        await foreach (WebResource resource in crawler.CrawlAsync(stop.Token).ConfigureAwait(false))
                        {
                            token.ThrowIfCancellationRequested();
                            if (resource == null || resource.Status < 200 || resource.Status >= 300) continue;
                            // Key a redirected page by the address its content came from, so the old and new addresses
                            // collapse into one object.
                            string? key = CrawlUrl.Normalize(String.IsNullOrEmpty(resource.FinalUrl) ? resource.Url : resource.FinalUrl, web.DropQueryParameters);
                            if (key == null || !seen.Add(key)) continue;
                            if (!await _Http.Policy.IsAllowedAsync(key, token).ConfigureAwait(false)) continue;

                            yield return new CrawledObject
                            {
                                Key = key,
                                Uri = key,
                                ContentType = MediaType(resource.ContentType),
                                SizeBytes = resource.ContentLength > 0 ? resource.ContentLength : (resource.Data?.Length ?? 0),
                                VersionToken = VersionOf(resource),
                                ModifiedUtc = resource.LastModified
                            };
                            if (seen.Count >= web.MaxPages) break;
                        }
                    }
                    finally
                    {
                        stop.Cancel();
                    }
                }
                if (seen.Count >= web.MaxPages) break;
            }
        }

        /// <inheritdoc />
        public async Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token)
        {
            CrawlHttpResponse response = await _Http.GetAsync(key, plan?.Web, token).ConfigureAwait(false);
            return new ResolvedContent { Bytes = response.Bytes };
        }

        #endregion

        #region Private-Methods

        private Settings BuildSettings(WebCrawlSettings web, string startUrl)
        {
            Settings settings = new Settings();
            settings.Crawl.StartUrl = startUrl;
            settings.Crawl.UserAgent = String.IsNullOrWhiteSpace(web.UserAgent) ? _Http.DefaultUserAgent : web.UserAgent;
            settings.Crawl.FollowLinks = web.FollowLinks;
            // CrawlSharp follows redirects one hop at a time, stops loops, and caps chains at MaxRedirects.
            settings.Crawl.FollowRedirects = true;
            settings.Crawl.IncludeSitemap = web.UseSitemap;
            settings.Crawl.IgnoreRobotsText = !web.RespectRobotsTxt;
            settings.Crawl.MaxCrawlDepth = web.MaxDepth;
            settings.Crawl.MaxParallelTasks = web.MaxParallelRequests;
            // RequestDelayMs is the pause between requests; ThrottleMs is only the back-off after a 429.
            settings.Crawl.RequestDelayMs = web.CrawlDelayMs;
            settings.Crawl.UseHeadlessBrowser = web.RenderJavaScript;
            settings.Crawl.RestrictToChildUrls = web.Scope == "ChildPaths";
            settings.Crawl.RestrictToSameSubdomain = web.Scope == "SameHost" || web.Scope == "ChildPaths";
            settings.Crawl.RestrictToSameRootDomain = web.Scope == "SameRootDomain";
            settings.Crawl.FollowExternalLinks = false;
            switch (web.Authentication)
            {
                case "Basic":
                    settings.Authentication.Type = AuthenticationTypeEnum.Basic;
                    settings.Authentication.Username = web.Username;
                    settings.Authentication.Password = web.Password;
                    break;
                case "Bearer":
                    settings.Authentication.Type = AuthenticationTypeEnum.BearerToken;
                    settings.Authentication.BearerToken = web.BearerToken;
                    break;
                case "ApiKey":
                    settings.Authentication.Type = AuthenticationTypeEnum.ApiKey;
                    settings.Authentication.ApiKeyHeader = web.ApiKeyHeader;
                    settings.Authentication.ApiKey = web.ApiKey;
                    break;
            }
            if (settings.Authentication.Type != AuthenticationTypeEnum.None)
            {
                // Credentials go only to the start URL's origin unless listed; a plan with several start URLs on different
                // origins authenticates to each of them, and never to any other site.
                settings.Authentication.CredentialOrigins = web.StartUrls
                    .Select(u => Uri.TryCreate(u, UriKind.Absolute, out Uri? parsed) ? parsed.GetLeftPart(UriPartial.Authority) : null)
                    .Where(o => o != null)
                    .Select(o => o!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            return settings;
        }

        private static string? VersionOf(WebResource resource)
        {
            if (!String.IsNullOrEmpty(resource.SHA256Hash)) return "sha256:" + resource.SHA256Hash;
            if (!String.IsNullOrEmpty(resource.ETag)) return "etag:" + resource.ETag;
            if (resource.LastModified != null) return "modified:" + resource.LastModified.Value.ToUniversalTime().ToString("o");
            return null;
        }

        private static string? MediaType(string? contentType)
        {
            if (String.IsNullOrWhiteSpace(contentType)) return null;
            return contentType.Split(';')[0].Trim().ToLowerInvariant();
        }

        #endregion
    }
}
