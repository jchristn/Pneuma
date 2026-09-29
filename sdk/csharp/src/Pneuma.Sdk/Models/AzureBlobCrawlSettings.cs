namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for an Azure Blob crawl plan.</summary>
    public class AzureBlobCrawlSettings
    {
        /// <summary>Storage account name.</summary>
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Blob endpoint; null for https://{account}.blob.core.windows.net/.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>Container.</summary>
        public string Container { get; set; } = string.Empty;

        /// <summary>Only blobs under this prefix.</summary>
        public string? Prefix { get; set; } = null;

        /// <summary>Include blobs under deeper prefixes.</summary>
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Account access key (write-only).</summary>
        public string? AccessKey { get; set; } = null;
    }
}
