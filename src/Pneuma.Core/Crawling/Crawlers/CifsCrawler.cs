namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using Blobject.CIFS;
    using Blobject.Core;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Crawls a CIFS (SMB 2 or 3) share with Blobject.CIFS (backed by OpenCIFS). A file is re-ingested when its size or
    /// last-write time changes.
    /// </summary>
    public class CifsCrawler : BlobCrawlerBase, ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.Cifs;

        /// <inheritdoc />
        public override string DisplayName => "CIFS (SMB) share";

        /// <inheritdoc />
        public override string Description => "Ingests the files in a Windows or Samba share (SMB 2 or 3). A file is re-ingested when its size or last-write time changes.";

        /// <inheritdoc />
        protected override bool SignInStep => true;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="maxDownloadBytes">Largest file read for ingestion; 0 for no limit.</param>
        public CifsCrawler(long maxDownloadBytes) : base(maxDownloadBytes)
        {
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            CifsCrawlSettings cifs = Settings(plan);
            CifsSettings settings = new CifsSettings(Host(plan), cifs.Port, cifs.Username ?? String.Empty, cifs.Password ?? String.Empty, ShareName(cifs));
            if (!String.IsNullOrWhiteSpace(cifs.Domain)) settings.Domain = cifs.Domain.Trim();
            return new CifsBlobClient(settings);
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            CifsCrawlSettings? cifs = plan?.Cifs;
            if (cifs == null) return "CIFS settings are required.";
            if (String.IsNullOrWhiteSpace(cifs.Host)) return "Set the host.";
            if (String.IsNullOrWhiteSpace(cifs.Share)) return "Set the share name.";
            if (String.IsNullOrWhiteSpace(cifs.Username)) return "Set the user name.";
            if (cifs.Port < 1 || cifs.Port > 65535) return "The port must be between 1 and 65535.";
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
            CifsCrawlSettings cifs = Settings(plan);
            string port = cifs.Port == 445 ? String.Empty : ":" + cifs.Port;
            return "smb://" + cifs.Host.Trim() + port + "/" + ShareName(cifs) + "/" + key;
        }

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan)
        {
            CifsCrawlSettings cifs = Settings(plan);
            return "\\\\" + cifs.Host + "\\" + cifs.Share + (String.IsNullOrWhiteSpace(cifs.Path) ? String.Empty : "\\" + cifs.Path);
        }

        private static CifsCrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.Cifs ?? throw new ArgumentException("A CIFS plan needs cifs settings.");
        }

        private static string ShareName(CifsCrawlSettings cifs)
        {
            return cifs.Share.Trim().Trim('/', '\\');
        }

        #endregion
    }
}
