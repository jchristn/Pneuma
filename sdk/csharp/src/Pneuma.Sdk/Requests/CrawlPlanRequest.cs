namespace Pneuma.Sdk.Requests
{
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>Body for creating or replacing a crawl plan. Only the settings object matching Type is used; secrets left null keep their stored values; names in ClearSecrets are removed.</summary>
    public class CrawlPlanRequest
    {
        /// <summary>Display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What the plan crawls.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>When false the schedule does not run the plan.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Web settings.</summary>
        public WebCrawlSettings? Web { get; set; } = null;

        /// <summary>Sitemap settings.</summary>
        public SitemapCrawlSettings? Sitemap { get; set; } = null;

        /// <summary>S3 settings.</summary>
        public S3CrawlSettings? S3 { get; set; } = null;

        /// <summary>CIFS settings.</summary>
        public CifsCrawlSettings? Cifs { get; set; } = null;

        /// <summary>NFS settings.</summary>
        public NfsCrawlSettings? Nfs { get; set; } = null;

        /// <summary>GitHub settings.</summary>
        public GitHubCrawlSettings? GitHub { get; set; } = null;

        /// <summary>Azure Blob settings.</summary>
        public AzureBlobCrawlSettings? AzureBlob { get; set; } = null;

        /// <summary>Google Cloud settings.</summary>
        public GoogleCloudCrawlSettings? GoogleCloud { get; set; } = null;

        /// <summary>Local folder settings.</summary>
        public LocalFolderCrawlSettings? LocalFolder { get; set; } = null;

        /// <summary>Filter.</summary>
        public CrawlFilter? Filter { get; set; } = null;

        /// <summary>Schedule.</summary>
        public CrawlSchedule? Schedule { get; set; } = null;

        /// <summary>Ingest new objects.</summary>
        public bool ProcessAdditions { get; set; } = true;

        /// <summary>Re-ingest changed objects.</summary>
        public bool ProcessUpdates { get; set; } = true;

        /// <summary>Delete links of objects gone from the source.</summary>
        public bool ProcessDeletions { get; set; } = false;

        /// <summary>Deletion limit.</summary>
        public double MaxDeletionFraction { get; set; } = 0.2;

        /// <summary>Retry failed objects.</summary>
        public bool RetryFailedObjects { get; set; } = true;

        /// <summary>Labels.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Tags.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>Days operations are kept.</summary>
        public int OperationRetentionDays { get; set; } = 30;

        /// <summary>Secret names to remove on a replace.</summary>
        public List<string>? ClearSecrets { get; set; } = null;
    }
}
