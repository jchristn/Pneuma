namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using Blobject.Core;
    using Blobject.NFS;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Crawls an NFSv3 export with Blobject.NFS (backed by OpenNFS, AUTH_SYS). A file is re-ingested when its size or
    /// modification time changes.
    /// </summary>
    public class NfsCrawler : BlobCrawlerBase, ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.Nfs;

        /// <inheritdoc />
        public override string DisplayName => "NFS export";

        /// <inheritdoc />
        public override string Description => "Ingests the files in an NFSv3 export. A file is re-ingested when its size or modification time changes.";

        /// <inheritdoc />
        protected override bool SignInStep => true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest file read for ingestion; 0 for no limit.</param>
        public NfsCrawler(long maxDownloadBytes) : base(maxDownloadBytes)
        {
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            NfsCrawlSettings nfs = Settings(plan);
            NfsSettings settings = new NfsSettings(Host(plan), nfs.UserId, nfs.GroupId, ExportPath(nfs), NfsVersionEnum.V3);
            settings.Port = nfs.Port;
            settings.MountPort = nfs.MountPort;
            return new NfsBlobClient(settings);
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            NfsCrawlSettings? nfs = plan?.Nfs;
            if (nfs == null) return "NFS settings are required.";
            if (String.IsNullOrWhiteSpace(nfs.Host)) return "Set the host.";
            if (String.IsNullOrWhiteSpace(nfs.Export)) return "Set the export path.";
            if (nfs.Port < 1 || nfs.Port > 65535) return "The NFS port must be between 1 and 65535.";
            if (nfs.MountPort < 0 || nfs.MountPort > 65535) return "The mount port must be 0 (ask the portmapper) or between 1 and 65535.";
            if (!String.Equals(nfs.Version, "V3", StringComparison.OrdinalIgnoreCase)) return "Only NFSv3 is supported.";
            return null;
        }

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan) => ContainerHost.Resolve(Settings(plan).Host);

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan) => Settings(plan).Port;

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan) => FolderPrefix(Settings(plan).Path);

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan) => Settings(plan).IncludeSubfolders;

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key)
        {
            NfsCrawlSettings nfs = Settings(plan);
            string port = nfs.Port == 2049 ? String.Empty : ":" + nfs.Port;
            return "nfs://" + nfs.Host.Trim() + port + ExportPath(nfs) + "/" + key;
        }

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan)
        {
            NfsCrawlSettings nfs = Settings(plan);
            string path = FolderPrefix(nfs.Path).TrimEnd('/');
            return nfs.Host + ":" + ExportPath(nfs) + (path.Length == 0 ? String.Empty : "/" + path);
        }

        private static NfsCrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.Nfs ?? throw new ArgumentException("An NFS plan needs nfs settings.");
        }

        private static string ExportPath(NfsCrawlSettings nfs)
        {
            return "/" + nfs.Export.Trim().Trim('/');
        }

        #endregion
    }
}
