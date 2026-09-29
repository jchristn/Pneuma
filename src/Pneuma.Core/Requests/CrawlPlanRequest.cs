namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Body for creating, replacing, testing, or previewing a crawl plan. Only the settings object that matches
    /// <see cref="Type"/> is used. On a replace (PUT) every configurable field is replaced; a secret left empty keeps
    /// the stored value, and a secret named in <see cref="ClearSecrets"/> is removed.
    /// </summary>
    public class CrawlPlanRequest
    {
        #region Public-Members

        /// <summary>Display name. Required; at most 256 characters.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>What the plan crawls. Cannot change after creation.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>When false the schedule does not run the plan. Default true.</summary>
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

        /// <summary>GitHub settings, when <see cref="Type"/> is GitHub.</summary>
        public GitHubCrawlSettings? GitHub { get; set; } = null;

        /// <summary>Azure Blob settings, when <see cref="Type"/> is AzureBlob.</summary>
        public AzureBlobCrawlSettings? AzureBlob { get; set; } = null;

        /// <summary>Google Cloud Storage settings, when <see cref="Type"/> is GoogleCloud.</summary>
        public GoogleCloudCrawlSettings? GoogleCloud { get; set; } = null;

        /// <summary>local folder settings, when <see cref="Type"/> is LocalFolder.</summary>
        public LocalFolderCrawlSettings? LocalFolder { get; set; } = null;

        /// <summary>Which objects are kept, or null for no filter.</summary>
        public CrawlFilter? Filter { get; set; } = null;

        /// <summary>When the plan runs on its own, or null for manual.</summary>
        public CrawlSchedule? Schedule { get; set; } = null;

        /// <summary>Ingest new objects. Default true.</summary>
        public bool ProcessAdditions { get; set; } = true;

        /// <summary>Re-ingest changed objects. Default true.</summary>
        public bool ProcessUpdates { get; set; } = true;

        /// <summary>Delete links of objects gone from the source. Default false.</summary>
        public bool ProcessDeletions { get; set; } = false;

        /// <summary>Largest share of links one run may delete before it is held. Default 0.2.</summary>
        public double MaxDeletionFraction { get; set; } = 0.2;

        /// <summary>Re-ingest objects whose last ingestion failed. Default true.</summary>
        public bool RetryFailedObjects { get; set; } = true;

        /// <summary>Labels stamped on every link the plan creates.</summary>
        public List<string>? Labels { get; set; } = null;

        /// <summary>Tags stamped on every link the plan creates.</summary>
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>Days operations are kept. Default 30.</summary>
        public int OperationRetentionDays { get; set; } = 30;

        /// <summary>Secret names (for example "password") to remove on a replace.</summary>
        public List<string>? ClearSecrets { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>Build a plan from the request.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <returns>The plan (not stored).</returns>
        public CrawlPlan ToPlan(string tenantId, string subjectId)
        {
            CrawlPlan plan = new CrawlPlan
            {
                TenantId = tenantId ?? String.Empty,
                SubjectId = subjectId ?? String.Empty,
                Name = Name ?? String.Empty,
                Type = Type,
                Enabled = Enabled,
                Filter = Filter ?? new CrawlFilter(),
                Schedule = Schedule ?? new CrawlSchedule(),
                ProcessAdditions = ProcessAdditions,
                ProcessUpdates = ProcessUpdates,
                ProcessDeletions = ProcessDeletions,
                MaxDeletionFraction = MaxDeletionFraction,
                RetryFailedObjects = RetryFailedObjects,
                Labels = Labels ?? new List<string>(),
                Tags = Tags ?? new Dictionary<string, string>(),
                OperationRetentionDays = OperationRetentionDays
            };
            switch (Type)
            {
                case CrawlPlanTypeEnum.Web: plan.Web = Web; break;
                case CrawlPlanTypeEnum.Sitemap: plan.Sitemap = Sitemap; break;
                case CrawlPlanTypeEnum.S3: plan.S3 = S3; break;
                case CrawlPlanTypeEnum.Cifs: plan.Cifs = Cifs; break;
                case CrawlPlanTypeEnum.Nfs: plan.Nfs = Nfs; break;
                case CrawlPlanTypeEnum.GitHub: plan.GitHub = GitHub; break;
                case CrawlPlanTypeEnum.AzureBlob: plan.AzureBlob = AzureBlob; break;
                case CrawlPlanTypeEnum.GoogleCloud: plan.GoogleCloud = GoogleCloud; break;
                case CrawlPlanTypeEnum.LocalFolder: plan.LocalFolder = LocalFolder; break;
            }
            return plan;
        }

        #endregion
    }
}
