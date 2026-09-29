namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for a Google Cloud Storage crawl plan.</summary>
    public class GoogleCloudCrawlSettings
    {
        /// <summary>Project id.</summary>
        public string ProjectId { get; set; } = string.Empty;

        /// <summary>Bucket.</summary>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Only objects under this prefix.</summary>
        public string? Prefix { get; set; } = null;

        /// <summary>Include objects under deeper prefixes.</summary>
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Custom endpoint (emulator or private endpoint), or null.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>Service account JSON key (write-only).</summary>
        public string? JsonCredentials { get; set; } = null;
    }
}
