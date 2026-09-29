namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using Blobject.Core;
    using Blobject.GoogleCloud;
    using Pneuma.Core.Enums;

    /// <summary>Crawls a Google Cloud Storage bucket. An object is re-ingested when its ETag changes.</summary>
    public class GoogleCloudCrawler : BlobCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.GoogleCloud;

        /// <inheritdoc />
        public override string DisplayName => "Google Cloud Storage bucket";

        /// <inheritdoc />
        public override string Description => "Ingests the objects in a Google Cloud Storage bucket under a prefix. An object is re-ingested when its ETag changes.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest object read for ingestion; 0 for no limit.</param>
        public GoogleCloudCrawler(long maxDownloadBytes) : base(maxDownloadBytes)
        {
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            GoogleCloudCrawlSettings gcs = Settings(plan);
            GcpBlobSettings settings = String.IsNullOrWhiteSpace(gcs.Endpoint)
                ? new GcpBlobSettings(gcs.ProjectId.Trim(), gcs.Bucket.Trim(), gcs.JsonCredentials ?? String.Empty)
                : new GcpBlobSettings(gcs.ProjectId.Trim(), gcs.Bucket.Trim(), gcs.JsonCredentials ?? String.Empty, gcs.Endpoint!.Trim());
            return new GcpBlobClient(settings);
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            GoogleCloudCrawlSettings? gcs = plan?.GoogleCloud;
            if (gcs == null) return "Google Cloud settings are required.";
            if (String.IsNullOrWhiteSpace(gcs.ProjectId)) return "Set the project id.";
            if (String.IsNullOrWhiteSpace(gcs.Bucket)) return "Set the bucket.";
            if (String.IsNullOrWhiteSpace(gcs.JsonCredentials)) return "Set the service account key.";
            if (!gcs.JsonCredentials!.TrimStart().StartsWith("{", StringComparison.Ordinal)) return "The service account key must be the JSON key file's contents.";
            if (!String.IsNullOrWhiteSpace(gcs.Endpoint) && !Uri.TryCreate(gcs.Endpoint, UriKind.Absolute, out Uri? _)) return "The endpoint is not a valid URL.";
            return null;
        }

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan)
        {
            GoogleCloudCrawlSettings gcs = Settings(plan);
            return String.IsNullOrWhiteSpace(gcs.Endpoint) ? "storage.googleapis.com" : new Uri(gcs.Endpoint!).Host;
        }

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan)
        {
            GoogleCloudCrawlSettings gcs = Settings(plan);
            return String.IsNullOrWhiteSpace(gcs.Endpoint) ? 443 : new Uri(gcs.Endpoint!).Port;
        }

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan)
        {
            string? prefix = Settings(plan).Prefix;
            return String.IsNullOrWhiteSpace(prefix) ? String.Empty : prefix!.TrimStart('/');
        }

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan) => Settings(plan).IncludeSubfolders;

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key) => "gs://" + Settings(plan).Bucket.Trim() + "/" + key.TrimStart('/');

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan)
        {
            GoogleCloudCrawlSettings gcs = Settings(plan);
            return "gs://" + gcs.Bucket + "/" + (gcs.Prefix ?? String.Empty);
        }

        private static GoogleCloudCrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.GoogleCloud ?? throw new ArgumentException("A Google Cloud plan needs googleCloud settings.");
        }

        #endregion
    }
}
