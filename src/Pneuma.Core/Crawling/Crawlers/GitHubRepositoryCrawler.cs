namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Runtime.CompilerServices;
    using System.Threading;
    using System.Threading.Tasks;
    using GitHubCrawler;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// Crawls the default branch of a GitHub repository with GitHubCrawler (the GitHub contents API). The library does
    /// not report file SHAs, so every run re-reads each file and unchanged content completes early by content hash.
    /// Requests go through the fetch-safety policy's connection checks and download limit.
    /// </summary>
    public class GitHubRepositoryCrawler : ICrawler
    {
        #region Public-Members

        /// <inheritdoc />
        public CrawlPlanTypeEnum Type => CrawlPlanTypeEnum.GitHub;

        /// <inheritdoc />
        public string DisplayName => "GitHub repository";

        /// <inheritdoc />
        public string Description => "Ingests the files on a GitHub repository's default branch, optionally under a folder. Each run re-reads every file; unchanged content is skipped by content hash.";

        #endregion

        #region Private-Members

        private readonly Func<HttpMessageHandler> _HandlerFactory;
        private readonly long _MaxDownloadBytes;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate over the fetch-safety policy.</summary>
        /// <param name="policy">Fetch-safety policy (connection checks and download limit).</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="policy"/> is null.</exception>
        public GitHubRepositoryCrawler(FetchSafetyPolicy policy)
            : this(() => (policy ?? throw new ArgumentNullException(nameof(policy))).CreateHandler(), policy.Settings.MaxDownloadBytes)
        {
        }

        /// <summary>Instantiate with a custom HTTP handler (for tests against a stub API).</summary>
        /// <param name="handlerFactory">Creates the handler for each crawler instance.</param>
        /// <param name="maxDownloadBytes">Largest file read for ingestion; 0 for no limit.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="handlerFactory"/> is null.</exception>
        public GitHubRepositoryCrawler(Func<HttpMessageHandler> handlerFactory, long maxDownloadBytes)
        {
            _HandlerFactory = handlerFactory ?? throw new ArgumentNullException(nameof(handlerFactory));
            _MaxDownloadBytes = Math.Max(0L, maxDownloadBytes);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public List<string> CheckSettings(CrawlPlan plan)
        {
            string? problem = SettingsProblem(plan?.GitHub);
            return problem == null ? new List<string>() : new List<string> { "gitHub." + problem };
        }

        /// <inheritdoc />
        public async Task<ConnectivityResult> TestAsync(CrawlPlan plan, CancellationToken token)
        {
            ConnectivityResult result = new ConnectivityResult();
            GitHubCrawlSettings? github = plan?.GitHub;
            string? problem = SettingsProblem(github);
            if (!result.Add("settings", problem == null, problem ?? "Repository " + Repository(github!) + ".")) return result;

            try
            {
                int listed = 0;
                using (GitHubRepoCrawler crawler = Create(github!))
                {
                    await foreach (string url in crawler.GetRepositoryContentsAsync(github!.RepositoryUrl.Trim(), token).ConfigureAwait(false))
                    {
                        listed++;
                        break;
                    }
                }
                result.Add("auth", true, String.IsNullOrEmpty(github!.Token) ? "No token; the rate limit is 60 requests an hour." : "Signed in with the access token.");
                result.Add("root", true, listed == 0 ? "The repository is empty." : "Listed the repository.");
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                string hint = e.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                    ? " Add an access token for a higher limit."
                    : e.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                        ? " Check the URL; a private repository needs an access token."
                        : String.Empty;
                result.Add("root", false, "Listing " + Repository(github!) + " failed: " + e.Message + hint);
            }
            return result;
        }

        /// <inheritdoc />
        public async IAsyncEnumerable<CrawledObject> EnumerateAsync(CrawlPlan plan, [EnumeratorCancellation] CancellationToken token)
        {
            GitHubCrawlSettings github = plan?.GitHub ?? throw new ArgumentException("A GitHub plan needs gitHub settings.");
            string? problem = SettingsProblem(github);
            if (problem != null) throw new ArgumentException(problem);
            string folder = String.IsNullOrWhiteSpace(github.Path) ? String.Empty : github.Path.Replace('\\', '/').Trim().Trim('/');

            using (GitHubRepoCrawler crawler = Create(github))
            {
                await foreach (string url in crawler.GetRepositoryContentsAsync(github.RepositoryUrl.Trim(), token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    RawUrl? raw = RawUrl.Parse(url);
                    if (raw == null) continue;
                    if (folder.Length > 0 && !raw.Path.StartsWith(folder + "/", StringComparison.Ordinal)) continue;
                    if (BlobCrawlerBase.IsSkippedName(raw.Path)) continue;
                    yield return new CrawledObject
                    {
                        Key = url,
                        Uri = "https://github.com/" + raw.Owner + "/" + raw.Repo + "/blob/" + raw.Branch + "/" + raw.Path,
                        Title = raw.Path.Split('/').Last(),
                        ContentType = CrawlContentTypes.FromName(raw.Path) ?? "text/plain",
                        VersionToken = null
                    };
                }
            }
        }

        /// <inheritdoc />
        /// <exception cref="HttpRequestException">Thrown when GitHub does not return the file.</exception>
        /// <exception cref="ContentTooLargeException">Thrown when the file exceeds the download limit.</exception>
        public async Task<ResolvedContent> OpenAsync(CrawlPlan plan, string key, CancellationToken token)
        {
            GitHubCrawlSettings github = plan?.GitHub ?? throw new ArgumentException("A GitHub plan needs gitHub settings.");
            using (GitHubRepoCrawler crawler = Create(github))
            {
                GitHubFileResponse response = await crawler.GetFileContentsAsync(key, token).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.OK)
                    throw new HttpRequestException("GET " + key + " returned " + (int)response.StatusCode + ".", null, response.StatusCode);
                byte[] content = response.Content ?? Array.Empty<byte>();
                if (_MaxDownloadBytes > 0 && content.LongLength > _MaxDownloadBytes)
                    throw new ContentTooLargeException(_MaxDownloadBytes, key + " is " + content.LongLength + " bytes; the limit is " + _MaxDownloadBytes + " (Ingestion.FetchSafety.MaxDownloadBytes).");
                return new ResolvedContent { Bytes = content };
            }
        }

        #endregion

        #region Private-Methods

        private GitHubRepoCrawler Create(GitHubCrawlSettings github)
        {
            return new GitHubRepoCrawler(_HandlerFactory(), String.IsNullOrWhiteSpace(github.Token) ? null : github.Token);
        }

        private static string? SettingsProblem(GitHubCrawlSettings? github)
        {
            if (github == null) return "GitHub settings are required.";
            if (String.IsNullOrWhiteSpace(github.RepositoryUrl)) return "Set the repository URL.";
            if (Repository(github) == null) return "The repository URL must look like https://github.com/owner/repo (or git@github.com:owner/repo.git).";
            return null;
        }

        private static string? Repository(GitHubCrawlSettings github)
        {
            string url = github.RepositoryUrl.Trim();
            if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) url = url.Substring(0, url.Length - 4);
            string? rest = null;
            if (url.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase)) rest = url.Substring("https://github.com/".Length);
            else if (url.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase)) rest = url.Substring("http://github.com/".Length);
            else if (url.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) rest = url.Substring("git@github.com:".Length);
            if (rest == null) return null;
            string[] parts = rest.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[0] + "/" + parts[1] : null;
        }

        #endregion
    }
}
