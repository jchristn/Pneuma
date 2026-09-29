namespace Pneuma.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>Settings for a Web crawl plan. Secret properties are write-only: send them to set, leave them null to keep the stored value.</summary>
    public class WebCrawlSettings
    {
        /// <summary>Pages the crawl starts from.</summary>
        public List<string> StartUrls { get; set; } = new List<string>();

        /// <summary>Follow links found on crawled pages.</summary>
        public bool FollowLinks { get; set; } = true;

        /// <summary>How many links deep to follow (1 to 20).</summary>
        public int MaxDepth { get; set; } = 3;

        /// <summary>Most pages one run may enumerate (1 to 100000).</summary>
        public int MaxPages { get; set; } = 1000;

        /// <summary>SameHost, SameRootDomain, or ChildPaths.</summary>
        public string Scope { get; set; } = "SameHost";

        /// <summary>Also read the site's sitemap.xml.</summary>
        public bool UseSitemap { get; set; } = true;

        /// <summary>Honor robots.txt.</summary>
        public bool RespectRobotsTxt { get; set; } = true;

        /// <summary>Pause between requests in milliseconds.</summary>
        public int CrawlDelayMs { get; set; } = 500;

        /// <summary>Concurrent requests to the site (1 to 16).</summary>
        public int MaxParallelRequests { get; set; } = 4;

        /// <summary>User-Agent header; null uses the ingestion User-Agent.</summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>Render pages in the headless browser.</summary>
        public bool RenderJavaScript { get; set; } = false;

        /// <summary>Query parameters removed when comparing URLs.</summary>
        public List<string> DropQueryParameters { get; set; } = new List<string> { "utm_*", "fbclid", "gclid" };

        /// <summary>None, Basic, Bearer, or ApiKey.</summary>
        public string Authentication { get; set; } = "None";

        /// <summary>User name for Basic authentication.</summary>
        public string? Username { get; set; } = null;

        /// <summary>Password for Basic authentication (write-only).</summary>
        public string? Password { get; set; } = null;

        /// <summary>Token for Bearer authentication (write-only).</summary>
        public string? BearerToken { get; set; } = null;

        /// <summary>Header that carries the API key.</summary>
        public string? ApiKeyHeader { get; set; } = null;

        /// <summary>API key value (write-only).</summary>
        public string? ApiKey { get; set; } = null;
    }
}
