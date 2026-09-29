namespace Pneuma.Core.Crawling
{
    using System.Collections.Generic;

    /// <summary>Settings for a Web crawl plan: which pages to follow, how politely, and how to sign in.</summary>
    public class WebCrawlSettings
    {
        #region Public-Members

        /// <summary>Pages the crawl starts from (1 to 50).</summary>
        [CrawlSetting("Start URLs", "Pages the crawl starts from, one per line. Every URL must be http or https.", Required = true)]
        public List<string> StartUrls { get; set; } = new List<string>();

        /// <summary>Follow links found on pages. Default true.</summary>
        [CrawlSetting("Follow links", "Follow links found on crawled pages. Off crawls only the start URLs (and the sitemap when enabled).")]
        public bool FollowLinks { get; set; } = true;

        /// <summary>How many links deep to follow from a start URL. Default 3; 1 to 20.</summary>
        [CrawlSetting("Maximum depth", "How many links deep to follow from a start URL. Default 3.", Min = 1, Max = 20)]
        public int MaxDepth { get; set; } = 3;

        /// <summary>Most pages one run may enumerate. Default 1000; 1 to 100000.</summary>
        [CrawlSetting("Maximum pages", "Most pages one run may enumerate. Default 1000.", Min = 1, Max = 100000)]
        public int MaxPages { get; set; } = 1000;

        /// <summary>Which links are in scope: SameHost (default), SameRootDomain, or ChildPaths.</summary>
        [CrawlSetting("Scope", "Which links are followed. SameHost stays on the start URL's host; SameRootDomain also allows its subdomains; ChildPaths stays under the start URL's path.", Options = new[] { "SameHost", "SameRootDomain", "ChildPaths" })]
        public string Scope { get; set; } = "SameHost";

        /// <summary>Also read the site's sitemap.xml. Default true.</summary>
        [CrawlSetting("Use sitemap", "Also read the site's sitemap.xml for pages to crawl.")]
        public bool UseSitemap { get; set; } = true;

        /// <summary>Honor robots.txt. Default true; turning it off is recorded in the audit log.</summary>
        [CrawlSetting("Respect robots.txt", "Skip pages the site's robots.txt disallows. Turning this off is recorded in the audit log.")]
        public bool RespectRobotsTxt { get; set; } = true;

        /// <summary>Pause between requests in milliseconds. Default 500; 0 to 60000.</summary>
        [CrawlSetting("Crawl delay (ms)", "Pause between requests to the site. Default 500.", Min = 0, Max = 60000)]
        public int CrawlDelayMs { get; set; } = 500;

        /// <summary>Concurrent requests to the site. Default 4; 1 to 16.</summary>
        [CrawlSetting("Parallel requests", "Requests the crawl may make to the site at once. Default 4.", Min = 1, Max = 16)]
        public int MaxParallelRequests { get; set; } = 4;

        /// <summary>User-Agent header; empty uses the ingestion User-Agent.</summary>
        [CrawlSetting("User agent", "User-Agent header sent while crawling. Empty uses the ingestion User-Agent.")]
        public string? UserAgent { get; set; } = null;

        /// <summary>Render pages in the headless browser while crawling. Default false.</summary>
        [CrawlSetting("Render JavaScript", "Render pages in the headless browser so links added by JavaScript are found. Slower.")]
        public bool RenderJavaScript { get; set; } = false;

        /// <summary>Query parameters dropped when comparing URLs (for example utm_*), so one page is not crawled twice.</summary>
        [CrawlSetting("Ignored query parameters", "Query parameters removed when comparing URLs, one per line; a trailing * matches a prefix (utm_*).")]
        public List<string> DropQueryParameters { get; set; } = new List<string> { "utm_*", "fbclid", "gclid" };

        /// <summary>Authentication: None (default), Basic, Bearer, or ApiKey.</summary>
        [CrawlSetting("Authentication", "How the crawler signs in to the site.", Options = new[] { "None", "Basic", "Bearer", "ApiKey" })]
        public string Authentication { get; set; } = "None";

        /// <summary>User name for Basic authentication.</summary>
        [CrawlSetting("User name", "User name for Basic authentication.")]
        public string? Username { get; set; } = null;

        /// <summary>Password for Basic authentication. Secret.</summary>
        [CrawlSetting("Password", "Password for Basic authentication. Stored encrypted and never returned.", Secret = true)]
        public string? Password { get; set; } = null;

        /// <summary>Token for Bearer authentication. Secret.</summary>
        [CrawlSetting("Bearer token", "Token for Bearer authentication. Stored encrypted and never returned.", Secret = true)]
        public string? BearerToken { get; set; } = null;

        /// <summary>Header name for API-key authentication.</summary>
        [CrawlSetting("API key header", "Header that carries the API key (for example X-Api-Key).")]
        public string? ApiKeyHeader { get; set; } = null;

        /// <summary>API key value. Secret.</summary>
        [CrawlSetting("API key", "API key value. Stored encrypted and never returned.", Secret = true)]
        public string? ApiKey { get; set; } = null;

        #endregion
    }
}
