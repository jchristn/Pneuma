namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Blobject.Core;
    using Blobject.Disk;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Crawls a folder on the Pneuma server. Only folders under the administrator's allowed roots
    /// (<see cref="CrawlingSettings.AllowedLocalRoots"/>) can be crawled, so a tenant cannot read the server's other
    /// files. A file is re-ingested when its size or modification time changes.
    /// </summary>
    public class LocalFolderCrawler : BlobCrawlerBase, ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public override CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.LocalFolder;

        /// <inheritdoc />
        public override string DisplayName => "Local folder";

        /// <inheritdoc />
        public override string Description => "Ingests the files in a folder on the Pneuma server, under a root the administrator allows. A file is re-ingested when its size or modification time changes.";

        /// <summary>The allowed roots, as full paths.</summary>
        public IReadOnlyList<string> AllowedRoots { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="allowedRoots">Folders plans may read (a plan's folder must be one of these or inside one).</param>
        /// <param name="maxDownloadBytes">Largest file read for ingestion; 0 for no limit.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="allowedRoots"/> is null.</exception>
        public LocalFolderCrawler(IEnumerable<string> allowedRoots, long maxDownloadBytes) : base(maxDownloadBytes)
        {
            if (allowedRoots == null) throw new ArgumentNullException(nameof(allowedRoots));
            AllowedRoots = allowedRoots.Where(r => !String.IsNullOrWhiteSpace(r)).Select(r => FullDirectory(r)).Distinct(PathComparer).ToList();
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public List<string> CheckSettings(CrawlPlan plan)
        {
            string? problem = SettingsProblem(plan);
            return problem == null ? new List<string>() : new List<string> { "localFolder." + problem };
        }

        /// <summary>True when a folder is one of the allowed roots or inside one.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>True when allowed.</returns>
        public bool IsAllowed(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder.Trim())) return false;
            string full = FullDirectory(folder);
            return AllowedRoots.Any(root => full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        /// <inheritdoc />
        protected override bool UsesNetwork => false;

        /// <inheritdoc />
        protected override BlobClientBase CreateClient(CrawlPlan plan)
        {
            return new DiskBlobClient(new DiskSettings(FullDirectory(Settings(plan).Folder)));
        }

        /// <inheritdoc />
        protected override string? SettingsProblem(CrawlPlan plan)
        {
            LocalFolderCrawlSettings? local = plan?.LocalFolder;
            if (local == null) return "Local folder settings are required.";
            if (AllowedRoots.Count == 0) return "Local folder plans are disabled on this server; an administrator allows folders in Crawling.AllowedLocalRoots.";
            if (String.IsNullOrWhiteSpace(local.Folder) || !Path.IsPathFullyQualified(local.Folder.Trim())) return "The folder must be an absolute path.";
            if (!IsAllowed(local.Folder)) return "The folder is not under an allowed root (" + String.Join(", ", AllowedRoots) + ").";
            if (!Directory.Exists(local.Folder.Trim())) return "The folder does not exist on the server.";
            return null;
        }

        /// <inheritdoc />
        protected override string Host(CrawlPlan plan) => "localhost";

        /// <inheritdoc />
        protected override int Port(CrawlPlan plan) => 0;

        /// <inheritdoc />
        protected override string Prefix(CrawlPlan plan) => String.Empty;

        /// <inheritdoc />
        protected override bool IncludeSubfolders(CrawlPlan plan) => Settings(plan).IncludeSubfolders;

        /// <inheritdoc />
        protected override string UriFor(CrawlPlan plan, string key)
        {
            return new Uri(Path.Combine(FullDirectory(Settings(plan).Folder), key.Replace('/', Path.DirectorySeparatorChar))).AbsoluteUri;
        }

        /// <inheritdoc />
        protected override string Describe(CrawlPlan plan) => Settings(plan).Folder;

        private static LocalFolderCrawlSettings Settings(CrawlPlan plan)
        {
            return plan?.LocalFolder ?? throw new ArgumentException("A local folder plan needs localFolder settings.");
        }

        private static string FullDirectory(string path)
        {
            string full = Path.GetFullPath(path.Trim());
            return full.EndsWith(Path.DirectorySeparatorChar) ? full : full + Path.DirectorySeparatorChar;
        }

        private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        #endregion
    }
}
