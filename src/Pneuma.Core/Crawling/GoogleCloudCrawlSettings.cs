namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for a Google Cloud Storage crawl plan: one bucket.</summary>
    public class GoogleCloudCrawlSettings
    {
        #region Public-Members

        /// <summary>Google Cloud project id.</summary>
        [CrawlSetting("Project id", "Google Cloud project id.", Required = true)]
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Bucket name.</summary>
        [CrawlSetting("Bucket", "Bucket to crawl.", Required = true)]
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Only objects whose names start with this prefix.</summary>
        [CrawlSetting("Prefix", "Only crawl objects under this prefix (for example policies/).")]
        public string? Prefix { get; set; } = null;

        /// <summary>Include objects under deeper prefixes. Default true.</summary>
        [CrawlSetting("Include subfolders", "Include objects under deeper prefixes.")]
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Custom endpoint (for example an emulator); empty for Google Cloud Storage.</summary>
        [CrawlSetting("Endpoint", "Custom storage endpoint, for an emulator or a private endpoint. Leave empty for Google Cloud Storage.")]
        public string? Endpoint { get; set; } = null;

        /// <summary>Service account key (JSON). Secret.</summary>
        [CrawlSetting("Service account key", "The service account's JSON key with read access to the bucket. Stored encrypted and never returned.", Secret = true, Required = true)]
        public string? JsonCredentials { get; set; } = null;

        #endregion
    }
}
