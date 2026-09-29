namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using Blobject.AmazonS3;
    using Blobject.Core;
    using Pneuma.Core.Enums;

    /// <summary>Crawls an Amazon S3 or S3-compatible bucket. An object is re-ingested when its ETag changes.</summary>
    public class S3Crawler : BlobCrawlerBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.S3;

        /// <inheritdoc />
        public override string DisplayName => "S3 bucket";

        /// <inheritdoc />
        public override string Description => "Ingests the objects in an Amazon S3 or S3-compatible bucket under a prefix. An object is re-ingested when its ETag changes.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest object read for ingestion; 0 for no limit.</param>
        public S3Crawler(long maxDownloadBytes) : base(maxDownloadBytes)
        {
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            S3CrawlSettings s3 = Settings(plan);
            string? accessKey = String.IsNullOrWhiteSpace(s3.AccessKey) ? null : s3.AccessKey;
            string? secretKey = String.IsNullOrWhiteSpace(s3.SecretKey) ? null : s3.SecretKey;
            AwsSettings settings = String.IsNullOrWhiteSpace(s3.Endpoint)
                ? new AwsSettings(accessKey, secretKey, s3.Region, s3.Bucket, s3.UseSsl)
                : new AwsSettings(EndpointUrl(s3), s3.UseSsl, accessKey, secretKey, s3.Region, s3.Bucket, EndpointUrl(s3) + "{bucket}/{key}");
            return new AmazonS3BlobClient(settings);
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            S3CrawlSettings? s3 = plan?.S3;
            if (s3 == null) return "S3 settings are required.";
            if (String.IsNullOrWhiteSpace(s3.Bucket)) return "Set the bucket.";
            if (String.IsNullOrWhiteSpace(s3.Region)) return "Set the region.";
            if (String.IsNullOrWhiteSpace(s3.AccessKey) != String.IsNullOrWhiteSpace(s3.SecretKey)) return "Set both the access key and the secret key, or neither for a public bucket.";
            if (!String.IsNullOrWhiteSpace(s3.Endpoint) && !Uri.TryCreate(EndpointUrl(s3), UriKind.Absolute, out Uri? _)) return "The endpoint is not a valid URL (for example http://minio:9000/).";
            return null;
        }

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan)
        {
            S3CrawlSettings s3 = Settings(plan);
            if (String.IsNullOrWhiteSpace(s3.Endpoint)) return "s3." + s3.Region + ".amazonaws.com";
            return new Uri(EndpointUrl(s3)).Host;
        }

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan)
        {
            S3CrawlSettings s3 = Settings(plan);
            if (String.IsNullOrWhiteSpace(s3.Endpoint)) return s3.UseSsl ? 443 : 80;
            return new Uri(EndpointUrl(s3)).Port;
        }

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan)
        {
            string? prefix = Settings(plan).Prefix;
            return String.IsNullOrWhiteSpace(prefix) ? String.Empty : prefix!.TrimStart('/');
        }

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan)
        {
            return Settings(plan).IncludeSubfolders;
        }

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key)
        {
            return "s3://" + Settings(plan).Bucket + "/" + key.TrimStart('/');
        }

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan)
        {
            S3CrawlSettings s3 = Settings(plan);
            return "s3://" + s3.Bucket + "/" + (s3.Prefix ?? String.Empty);
        }

        private static S3CrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.S3 ?? throw new ArgumentException("An S3 plan needs s3 settings.");
        }

        private static string EndpointUrl(S3CrawlSettings s3)
        {
            string endpoint = s3.Endpoint!.Trim();
            if (!endpoint.Contains("://", StringComparison.Ordinal)) endpoint = (s3.UseSsl ? "https://" : "http://") + endpoint;
            return endpoint.EndsWith("/", StringComparison.Ordinal) ? endpoint : endpoint + "/";
        }

        #endregion
    }
}
