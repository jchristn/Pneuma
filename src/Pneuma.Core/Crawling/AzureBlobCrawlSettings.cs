namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for an Azure Blob crawl plan: one storage container.</summary>
    public class AzureBlobCrawlSettings
    {
        #region Public-Members

        /// <summary>Storage account name.</summary>
        [CrawlSetting("Account name", "Storage account name.", Required = true)]
        public string AccountName { get; set; } = string.Empty;

        /// <summary>Blob service endpoint; empty for https://{account}.blob.core.windows.net/.</summary>
        [CrawlSetting("Endpoint", "Blob service endpoint. Leave empty for https://{account}.blob.core.windows.net/; set it for sovereign clouds or the Azurite emulator (for example http://127.0.0.1:10000/devstoreaccount1).")]
        public string? Endpoint { get; set; } = null;

        /// <summary>Container name.</summary>
        [CrawlSetting("Container", "Container to crawl.", Required = true)]
        public string Container { get; set; } = string.Empty;

        /// <summary>Only blobs whose names start with this prefix.</summary>
        [CrawlSetting("Prefix", "Only crawl blobs under this prefix (for example policies/).")]
        public string? Prefix { get; set; } = null;

        /// <summary>Include blobs under deeper prefixes. Default true.</summary>
        [CrawlSetting("Include subfolders", "Include blobs under deeper prefixes.")]
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Storage account access key. Secret.</summary>
        [CrawlSetting("Access key", "Storage account access key. Stored encrypted and never returned.", Secret = true, Required = true)]
        public string? AccessKey { get; set; } = null;

        #endregion
    }
}
