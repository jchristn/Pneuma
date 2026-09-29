namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for a GitHub crawl plan (the default branch of one repository).</summary>
    public class GitHubCrawlSettings
    {
        /// <summary>Repository URL, for example https://github.com/owner/repo.</summary>
        public string RepositoryUrl { get; set; } = string.Empty;

        /// <summary>Only files under this folder.</summary>
        public string? Path { get; set; } = null;

        /// <summary>Personal access token (write-only).</summary>
        public string? Token { get; set; } = null;
    }
}
