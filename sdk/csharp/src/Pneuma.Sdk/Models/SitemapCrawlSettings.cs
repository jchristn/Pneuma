namespace Pneuma.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>Settings for a Sitemap crawl plan.</summary>
    public class SitemapCrawlSettings
    {
        /// <summary>Sitemap or sitemap index URLs.</summary>
        public List<string> SitemapUrls { get; set; } = new List<string>();

        /// <summary>Most URLs one run may enumerate.</summary>
        public int MaxUrls { get; set; } = 10000;

        /// <summary>Use each URL's lastmod as its version.</summary>
        public bool UseLastModified { get; set; } = true;
    }
}
