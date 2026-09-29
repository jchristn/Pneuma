namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A source a subject is kept in sync with: what to crawl (one of the per-type settings objects, matching
    /// <see cref="Type"/>), which objects to keep, when to run, and what to do with added, changed, and removed objects.
    /// Each run is a crawl operation; each object the plan has seen is a crawl object linked to the subject link that
    /// holds its content.
    /// </summary>
    public class CrawlPlan
    {
        #region Public-Members

        /// <summary>Crawl plan identifier (prefix "cpl_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>The subject the plan keeps in sync.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>Display name. At most 256 characters.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>What the plan crawls.</summary>
        public CrawlPlanTypeEnum Type { get; set; } = CrawlPlanTypeEnum.Web;

        /// <summary>When false the schedule does not run the plan (a manual start still works).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Whether an operation is in progress.</summary>
        public CrawlPlanStatusEnum Status { get; set; } = CrawlPlanStatusEnum.Idle;

        /// <summary>Web settings, when <see cref="Type"/> is Web.</summary>
        public WebCrawlSettings? Web { get; set; } = null;

        /// <summary>Sitemap settings, when <see cref="Type"/> is Sitemap.</summary>
        public SitemapCrawlSettings? Sitemap { get; set; } = null;

        /// <summary>S3 settings, when <see cref="Type"/> is S3.</summary>
        public S3CrawlSettings? S3 { get; set; } = null;

        /// <summary>CIFS settings, when <see cref="Type"/> is Cifs.</summary>
        public CifsCrawlSettings? Cifs { get; set; } = null;

        /// <summary>NFS settings, when <see cref="Type"/> is Nfs.</summary>
        public NfsCrawlSettings? Nfs { get; set; } = null;

        /// <summary>GitHub settings, when <see cref="Type"/> is GitHub.</summary>
        public GitHubCrawlSettings? GitHub { get; set; } = null;

        /// <summary>Azure Blob settings, when <see cref="Type"/> is AzureBlob.</summary>
        public AzureBlobCrawlSettings? AzureBlob { get; set; } = null;

        /// <summary>Google Cloud Storage settings, when <see cref="Type"/> is GoogleCloud.</summary>
        public GoogleCloudCrawlSettings? GoogleCloud { get; set; } = null;

        /// <summary>local folder settings, when <see cref="Type"/> is LocalFolder.</summary>
        public LocalFolderCrawlSettings? LocalFolder { get; set; } = null;

        /// <summary>Which enumerated objects are kept. Never null.</summary>
        public CrawlFilter Filter
        {
            get { return _Filter; }
            set { _Filter = value ?? new CrawlFilter(); }
        }

        /// <summary>When the plan runs on its own. Never null.</summary>
        public CrawlSchedule Schedule
        {
            get { return _Schedule; }
            set { _Schedule = value ?? new CrawlSchedule(); }
        }

        /// <summary>Ingest objects that are new since the last run. Default true.</summary>
        public bool ProcessAdditions { get; set; } = true;

        /// <summary>Re-ingest objects that changed since the last run. Default true.</summary>
        public bool ProcessUpdates { get; set; } = true;

        /// <summary>
        /// Delete the links of objects that disappeared from the source. Default false, so a misconfigured plan cannot
        /// empty a subject; disappeared objects are marked Missing instead.
        /// </summary>
        public bool ProcessDeletions { get; set; } = false;

        /// <summary>
        /// Largest share of the plan's links one run may delete before it is held for confirmation. Default 0.2;
        /// clamped to [0, 1]. 1 never holds.
        /// </summary>
        public double MaxDeletionFraction
        {
            get { return _MaxDeletionFraction; }
            set { _MaxDeletionFraction = Math.Clamp(value, 0.0, 1.0); }
        }

        /// <summary>Re-ingest objects whose last ingestion failed, even when unchanged. Default true.</summary>
        public bool RetryFailedObjects { get; set; } = true;

        /// <summary>Labels stamped on every link (and so every chunk) the plan creates. Never null.</summary>
        public List<string> Labels
        {
            get { return _Labels; }
            set { _Labels = value ?? new List<string>(); }
        }

        /// <summary>Tags stamped on every link (and so every chunk) the plan creates. Never null.</summary>
        public Dictionary<string, string> Tags
        {
            get { return _Tags; }
            set { _Tags = value ?? new Dictionary<string, string>(); }
        }

        /// <summary>Days operations are kept before they are pruned. Default 30; clamped to [1, 3650].</summary>
        public int OperationRetentionDays
        {
            get { return _OperationRetentionDays; }
            set { _OperationRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>The most recent operation, or null.</summary>
        public string? LastOperationId { get; set; } = null;

        /// <summary>When the most recent operation started, or null.</summary>
        public DateTime? LastRunUtc { get; set; } = null;

        /// <summary>When the most recent operation that fully succeeded finished, or null.</summary>
        public DateTime? LastSuccessUtc { get; set; } = null;

        /// <summary>When the schedule runs the plan next, or null for a manual or disabled plan.</summary>
        public DateTime? NextRunUtc { get; set; } = null;

        /// <summary>When the current run's claim lapses (a server that stops mid-run releases the plan then), or null when idle.</summary>
        public DateTime? ClaimExpiresUtc { get; set; } = null;

        /// <summary>Names of the secrets stored for the plan (their values are never returned). Read-only in responses.</summary>
        public List<string> SecretsSet { get; set; } = new List<string>();

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateCrawlPlanId();
        private CrawlFilter _Filter = new CrawlFilter();
        private CrawlSchedule _Schedule = new CrawlSchedule();
        private double _MaxDeletionFraction = 0.2;
        private List<string> _Labels = new List<string>();
        private Dictionary<string, string> _Tags = new Dictionary<string, string>();
        private int _OperationRetentionDays = 30;

        #endregion

        #region Public-Methods

        /// <summary>The settings object for the plan's type, or null when it is missing.</summary>
        /// <returns>The settings object.</returns>
        public object? SettingsObject()
        {
            switch (Type)
            {
                case CrawlPlanTypeEnum.Web: return Web;
                case CrawlPlanTypeEnum.Sitemap: return Sitemap;
                case CrawlPlanTypeEnum.S3: return S3;
                case CrawlPlanTypeEnum.Cifs: return Cifs;
                case CrawlPlanTypeEnum.Nfs: return Nfs;
                case CrawlPlanTypeEnum.GitHub: return GitHub;
                case CrawlPlanTypeEnum.AzureBlob: return AzureBlob;
                case CrawlPlanTypeEnum.GoogleCloud: return GoogleCloud;
                case CrawlPlanTypeEnum.LocalFolder: return LocalFolder;
                default: return null;
            }
        }

        /// <summary>Set the settings object for the plan's type (clearing the others).</summary>
        /// <param name="settings">A settings object of the class that matches <see cref="Type"/>.</param>
        /// <exception cref="ArgumentException">Thrown when the object's class does not match the type.</exception>
        public void SetSettingsObject(object? settings)
        {
            Web = null;
            Sitemap = null;
            S3 = null;
            Cifs = null;
            Nfs = null;
            GitHub = null;
            AzureBlob = null;
            GoogleCloud = null;
            LocalFolder = null;
            if (settings == null) return;
            switch (Type)
            {
                case CrawlPlanTypeEnum.Web: Web = settings as WebCrawlSettings ?? throw new ArgumentException("Web plans take WebCrawlSettings."); break;
                case CrawlPlanTypeEnum.Sitemap: Sitemap = settings as SitemapCrawlSettings ?? throw new ArgumentException("Sitemap plans take SitemapCrawlSettings."); break;
                case CrawlPlanTypeEnum.S3: S3 = settings as S3CrawlSettings ?? throw new ArgumentException("S3 plans take S3CrawlSettings."); break;
                case CrawlPlanTypeEnum.Cifs: Cifs = settings as CifsCrawlSettings ?? throw new ArgumentException("CIFS plans take CifsCrawlSettings."); break;
                case CrawlPlanTypeEnum.Nfs: Nfs = settings as NfsCrawlSettings ?? throw new ArgumentException("NFS plans take NfsCrawlSettings."); break;
                case CrawlPlanTypeEnum.GitHub: GitHub = settings as GitHubCrawlSettings ?? throw new ArgumentException("GitHub plans take GitHubCrawlSettings."); break;
                case CrawlPlanTypeEnum.AzureBlob: AzureBlob = settings as AzureBlobCrawlSettings ?? throw new ArgumentException("AzureBlob plans take AzureBlobCrawlSettings."); break;
                case CrawlPlanTypeEnum.GoogleCloud: GoogleCloud = settings as GoogleCloudCrawlSettings ?? throw new ArgumentException("GoogleCloud plans take GoogleCloudCrawlSettings."); break;
                case CrawlPlanTypeEnum.LocalFolder: LocalFolder = settings as LocalFolderCrawlSettings ?? throw new ArgumentException("LocalFolder plans take LocalFolderCrawlSettings."); break;
            }
        }

        #endregion
    }
}
