namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;

    /// <summary>Settings for a Sitemap crawl plan: which sitemaps to read and which of their URLs to keep.</summary>
    public class SitemapCrawlSettings
    {
        #region Public-Members

        /// <summary>Sitemap or sitemap-index URLs, or site roots whose robots.txt names the sitemaps.</summary>
        [CrawlSetting("Sitemap URLs", "Sitemap or sitemap-index URLs, one per line. A site root (https://example.com/) reads the sitemaps its robots.txt lists.", Required = true)]
        public List<string> SitemapUrls { get; set; } = new List<string>();

        /// <summary>Most URLs one run may take. Default 10000; 1 to 200000.</summary>
        [CrawlSetting("Maximum URLs", "Most URLs one run may take from the sitemaps. Default 10000.", Min = 1, Max = 200000)]
        public int MaxUrls { get; set; } = 10000;

        /// <summary>Use each URL's lastmod as its version, so unchanged pages are skipped without fetching them. Default true.</summary>
        [CrawlSetting("Use last-modified dates", "Treat a URL as changed only when its sitemap lastmod changes, so unchanged pages are not fetched. Off re-ingests every URL each run (unchanged content still completes early).")]
        public bool UseLastModified { get; set; } = true;

        #endregion
    }
}
