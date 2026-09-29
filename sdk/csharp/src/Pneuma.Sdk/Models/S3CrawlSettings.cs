namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for an S3 (or S3-compatible) crawl plan.</summary>
    public class S3CrawlSettings
    {
        /// <summary>Endpoint for S3-compatible storage; null for AWS.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>Use HTTPS.</summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>Region.</summary>
        public string Region { get; set; } = "us-west-1";

        /// <summary>Bucket name.</summary>
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Key prefix to crawl.</summary>
        public string? Prefix { get; set; } = null;

        /// <summary>Include keys under sub-prefixes.</summary>
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Access key id.</summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>Secret access key (write-only).</summary>
        public string? SecretKey { get; set; } = null;
    }
}
