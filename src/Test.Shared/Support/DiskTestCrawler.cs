namespace Test.Shared.Support
{
    using System;
    using Blobject.Core;
    using Blobject.Disk;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Enums;

    /// <summary>
    /// The shared bucket and share crawler logic over a local directory (Blobject.Disk), registered as the S3 type so
    /// the listing, prefix, subfolder, version-token, and size-limit behavior can be tested without a storage server.
    /// The plan's S3 bucket is ignored; <see cref="Directory"/> is the root and the S3 prefix is the folder.
    /// </summary>
    public sealed class DiskTestCrawler : BlobCrawlerBase
    {
        #region Public-Members

        /// <summary>The root directory.</summary>
        public string Directory { get; }

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.S3;

        /// <inheritdoc />
        public override string DisplayName => "Disk (test)";

        /// <inheritdoc />
        public override string Description => "A local directory standing in for a bucket.";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="directory">Root directory.</param>
        /// <param name="maxDownloadBytes">Download limit; 0 for none.</param>
        public DiskTestCrawler(string directory, long maxDownloadBytes = 0) : base(maxDownloadBytes)
        {
            Directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan) => new DiskBlobClient(new DiskSettings(Directory));

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan) => plan?.S3 == null ? "S3 settings are required." : null;

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan) => "127.0.0.1";

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan) => 9;

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan) => FolderPrefix(plan.S3!.Prefix);

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan) => plan.S3!.IncludeSubfolders;

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key) => "file://" + key;

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan) => Directory;

        #endregion
    }
}
