namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>URL helpers for web and sitemap crawlers.</summary>
    public static class CrawlUrl
    {
        #region Public-Methods

        /// <summary>
        /// Normalize a URL into a crawl key: lower-case scheme and host, no fragment, no default port, and the query
        /// parameters named in <paramref name="dropParameters"/> removed (a trailing <c>*</c> matches a prefix).
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="dropParameters">Parameters to drop, or null.</param>
        /// <returns>The key, or null when the URL is not absolute http or https.</returns>
        public static string? Normalize(string? url, IEnumerable<string>? dropParameters)
        {
            if (String.IsNullOrWhiteSpace(url)) return null;
            Uri? uri;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

            List<string> drops = (dropParameters ?? Enumerable.Empty<string>()).Where(d => !String.IsNullOrWhiteSpace(d)).Select(d => d.Trim()).ToList();
            string query = String.Empty;
            if (!String.IsNullOrEmpty(uri.Query) && uri.Query.Length > 1)
            {
                List<string> kept = uri.Query.Substring(1).Split('&', StringSplitOptions.RemoveEmptyEntries)
                    .Where(pair => !drops.Any(d => Matches(d, pair.Split('=')[0])))
                    .ToList();
                if (kept.Count > 0) query = "?" + String.Join("&", kept);
            }

            string port = uri.IsDefaultPort ? String.Empty : ":" + uri.Port;
            string path = String.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            return uri.Scheme.ToLowerInvariant() + "://" + uri.Host.ToLowerInvariant() + port + path + query;
        }

        #endregion

        #region Private-Methods

        private static bool Matches(string pattern, string name)
        {
            if (pattern.EndsWith("*", StringComparison.Ordinal)) return name.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            return String.Equals(pattern, name, StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
