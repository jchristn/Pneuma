namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for an S3 crawl plan: an Amazon S3 or S3-compatible bucket and prefix.</summary>
    public class S3CrawlSettings
    {
        #region Public-Members

        /// <summary>Endpoint host for an S3-compatible store (MinIO, Less3, R2); empty uses AWS.</summary>
        [CrawlSetting("Endpoint", "Host (and port) of an S3-compatible store such as MinIO or Less3, for example less3:8000. Leave empty for Amazon S3.")]
        public string? Endpoint { get; set; } = null;

        /// <summary>Use HTTPS to reach the endpoint. Default true.</summary>
        [CrawlSetting("Use HTTPS", "Connect to the endpoint over HTTPS.")]
        public bool UseSsl { get; set; } = true;

        /// <summary>AWS region. Default us-west-1.</summary>
        [CrawlSetting("Region", "AWS region of the bucket (also required by most S3-compatible stores). Default us-west-1.", Required = true)]
        public string Region { get; set; } = "us-west-1";

        /// <summary>Bucket name.</summary>
        [CrawlSetting("Bucket", "Bucket to crawl.", Required = true)]
        public string Bucket { get; set; } = string.Empty;

        /// <summary>Only objects whose keys start with this prefix.</summary>
        [CrawlSetting("Prefix", "Only crawl objects whose keys start with this prefix (for example docs/).")]
        public string? Prefix { get; set; } = null;

        /// <summary>Include objects under sub-prefixes ("folders"). Default true.</summary>
        [CrawlSetting("Include subfolders", "Include objects under deeper prefixes. Off crawls only the prefix's top level.")]
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Access key id.</summary>
        [CrawlSetting("Access key", "Access key id. Leave empty, with the secret key, for a public bucket.")]
        public string? AccessKey { get; set; } = null;

        /// <summary>Secret access key. Secret.</summary>
        [CrawlSetting("Secret key", "Secret access key. Stored encrypted and never returned.", Secret = true)]
        public string? SecretKey { get; set; } = null;

        #endregion
    }
}
