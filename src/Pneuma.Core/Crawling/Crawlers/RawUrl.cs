namespace Pneuma.Core.Crawling.Crawlers
{
    using System;

    /// <summary>The parts of a raw.githubusercontent.com file URL: owner, repository, branch, and path.</summary>
    public class RawUrl
    {
        #region Public-Members

        /// <summary>Repository owner.</summary>
        public string Owner { get; set; } = String.Empty;

        /// <summary>Repository name.</summary>
        public string Repo { get; set; } = String.Empty;

        /// <summary>Branch (or ref).</summary>
        public string Branch { get; set; } = String.Empty;

        /// <summary>File path within the repository, with forward slashes.</summary>
        public string Path { get; set; } = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>Parse a raw file URL (https://raw.githubusercontent.com/owner/repo/branch/path).</summary>
        /// <param name="url">The URL.</param>
        /// <returns>The parts, or null when the URL is not a raw file URL.</returns>
        public static RawUrl? Parse(string? url)
        {
            Uri? uri;
            if (String.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri)) return null;
            string[] parts = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/').Split('/', 4);
            if (parts.Length < 4 || parts[3].Length == 0) return null;
            return new RawUrl { Owner = parts[0], Repo = parts[1], Branch = parts[2], Path = parts[3] };
        }

        #endregion
    }
}
