namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for a GitHub crawl plan: the files of one repository's default branch.</summary>
    public class GitHubCrawlSettings
    {
        #region Public-Members

        /// <summary>Repository URL (https://github.com/owner/repo, with or without .git, or git@github.com:owner/repo.git).</summary>
        [CrawlSetting("Repository URL", "The repository to crawl, for example https://github.com/owner/repo. The default branch is crawled.", Required = true)]
        public string RepositoryUrl { get; set; } = string.Empty;

        /// <summary>Only files under this folder of the repository.</summary>
        [CrawlSetting("Path", "Only crawl files under this folder of the repository (for example docs).")]
        public string? Path { get; set; } = null;

        /// <summary>Personal access token. Secret.</summary>
        [CrawlSetting("Access token", "Personal access token for a private repository and a higher rate limit (60 requests an hour without one, 5000 with). Stored encrypted and never returned.", Secret = true)]
        public string? Token { get; set; } = null;

        #endregion
    }
}
