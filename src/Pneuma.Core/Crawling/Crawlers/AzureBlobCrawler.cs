namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using Blobject.AzureBlob;
    using Blobject.Core;
    using Pneuma.Core.Enums;

    /// <summary>Crawls an Azure Blob Storage container. A blob is re-ingested when its ETag changes.</summary>
    public class AzureBlobCrawler : BlobCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.AzureBlob;

        /// <inheritdoc />
        public override string DisplayName => "Azure Blob container";

        /// <inheritdoc />
        public override string Description => "Ingests the blobs in an Azure Blob Storage container under a prefix. A blob is re-ingested when its ETag changes.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest blob read for ingestion; 0 for no limit.</param>
        public AzureBlobCrawler(long maxDownloadBytes) : base(maxDownloadBytes)
        {
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            AzureBlobCrawlSettings azure = Settings(plan);
            return new AzureBlobClient(new AzureBlobSettings(azure.AccountName.Trim(), azure.AccessKey ?? String.Empty, EndpointUrl(azure), azure.Container.Trim()));
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            AzureBlobCrawlSettings? azure = plan?.AzureBlob;
            if (azure == null) return "Azure Blob settings are required.";
            if (String.IsNullOrWhiteSpace(azure.AccountName)) return "Set the account name.";
            if (String.IsNullOrWhiteSpace(azure.Container)) return "Set the container.";
            if (String.IsNullOrWhiteSpace(azure.AccessKey)) return "Set the access key.";
            if (!Uri.TryCreate(EndpointUrl(azure), UriKind.Absolute, out Uri? _)) return "The endpoint is not a valid URL.";
            return null;
        }

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan) => new Uri(EndpointUrl(Settings(plan))).Host;

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan) => new Uri(EndpointUrl(Settings(plan))).Port;

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan)
        {
            string? prefix = Settings(plan).Prefix;
            return String.IsNullOrWhiteSpace(prefix) ? String.Empty : prefix!.TrimStart('/');
        }

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan) => Settings(plan).IncludeSubfolders;

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key)
        {
            return EndpointUrl(Settings(plan)) + Settings(plan).Container.Trim() + "/" + key.TrimStart('/');
        }

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan)
        {
            AzureBlobCrawlSettings azure = Settings(plan);
            return "container " + azure.Container + " in " + azure.AccountName + (String.IsNullOrWhiteSpace(azure.Prefix) ? String.Empty : " under " + azure.Prefix);
        }

        private static AzureBlobCrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.AzureBlob ?? throw new ArgumentException("An Azure Blob plan needs azureBlob settings.");
        }

        private static string EndpointUrl(AzureBlobCrawlSettings azure)
        {
            string endpoint = String.IsNullOrWhiteSpace(azure.Endpoint) ? "https://" + azure.AccountName.Trim() + ".blob.core.windows.net/" : azure.Endpoint!.Trim();
            return endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/";
        }

        #endregion
    }
}
