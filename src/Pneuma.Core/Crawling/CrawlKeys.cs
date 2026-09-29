namespace Pneuma.Core.Crawling
{
    using System;
    using System.Security.Cryptography;
    using System.Text;

    /// <summary>Key helpers for crawled links.</summary>
    public static class CrawlKeys
    {
        #region Public-Members

        /// <summary>Longest external key a link stores.</summary>
        public const int MaxLinkKeyLength = 256;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The external key a crawled link carries: <c>crawl:{planId}:{key}</c>, so two plans (or a plan and pushed
        /// content) in one subject never collide. Keys that would exceed <see cref="MaxLinkKeyLength"/> keep a readable
        /// prefix and end with a SHA-256 of the object key.
        /// </summary>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="key">The object's key.</param>
        /// <returns>The link external key.</returns>
        public static string LinkExternalKey(string planId, string key)
        {
            string full = "crawl:" + (planId ?? String.Empty) + ":" + (key ?? String.Empty);
            if (full.Length <= MaxLinkKeyLength) return full;
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? String.Empty))).ToLowerInvariant();
            string suffix = "#" + hash;
            return full.Substring(0, MaxLinkKeyLength - suffix.Length) + suffix;
        }

        /// <summary>A display title for an object with none: the last path segment of its key, or the key.</summary>
        /// <param name="key">The object's key.</param>
        /// <returns>The title.</returns>
        public static string TitleFor(string key)
        {
            if (String.IsNullOrEmpty(key)) return String.Empty;
            string trimmed = key.TrimEnd('/', '\\');
            int slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
            string last = slash >= 0 ? trimmed.Substring(slash + 1) : trimmed;
            return String.IsNullOrEmpty(last) ? key : last;
        }

        #endregion
    }
}
