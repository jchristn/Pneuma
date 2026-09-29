namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>A source a subject is kept in sync with. Secret values are never returned; SecretsSet names the ones stored.</summary>
    public class CrawlPlan
    {
        /// <summary>Crawl plan identifier (prefix "cpl_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The subject the plan keeps in sync.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>Display name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What the plan crawls.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>When false the schedule does not run the plan.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Whether an operation is in progress.</summary>
        public CrawlPlanStatusEnum Status { get; set; } = CrawlPlanStatusEnum.Idle;

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

        /// <summary>Which objects are kept.</summary>
        public CrawlFilter Filter { get; set; } = new CrawlFilter();

        /// <summary>When the plan runs.</summary>
        public CrawlSchedule Schedule { get; set; } = new CrawlSchedule();

        /// <summary>Ingest new objects.</summary>
        public bool ProcessAdditions { get; set; } = true;

        /// <summary>Re-ingest changed objects.</summary>
        public bool ProcessUpdates { get; set; } = true;

        /// <summary>Delete links of objects gone from the source.</summary>
        public bool ProcessDeletions { get; set; } = false;

        /// <summary>Largest share of links one run may delete before it is held.</summary>
        public double MaxDeletionFraction { get; set; } = 0.2;

        /// <summary>Re-ingest objects whose last ingestion failed.</summary>
        public bool RetryFailedObjects { get; set; } = true;

        /// <summary>Labels stamped on every link the plan creates.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Tags stamped on every link the plan creates.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>Days operations are kept.</summary>
        public int OperationRetentionDays { get; set; } = 30;

        /// <summary>The most recent operation.</summary>
        public string? LastOperationId { get; set; } = null;

        /// <summary>When the most recent operation started.</summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>When the most recent fully successful operation finished.</summary>
        public DateTime? LastSuccessUtc { get; set; } = null;

        /// <summary>When the schedule runs the plan next.</summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>When the current run's claim lapses.</summary>
        public DateTime? ClaimExpiresUtc { get; set; } = null;

        /// <summary>Names of the secrets stored for the plan.</summary>
        public List<string> SecretsSet { get; set; } = new List<string>();

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
