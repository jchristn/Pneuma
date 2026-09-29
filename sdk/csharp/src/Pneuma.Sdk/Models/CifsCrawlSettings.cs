namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for a CIFS (SMB) share crawl plan.</summary>
    public class CifsCrawlSettings
    {
        /// <summary>Server host name or address.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>Share name.</summary>
        public string Share { get; set; } = string.Empty;

        /// <summary>SMB port (default 445).</summary>
        public int Port { get; set; } = 445;

        /// <summary>Folder within the share.</summary>
        public string? Path { get; set; } = null;

        /// <summary>Include subfolders.</summary>
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Windows domain.</summary>
        public string? Domain { get; set; } = null;

        /// <summary>User name.</summary>
        public string? Username { get; set; } = null;

        /// <summary>Password (write-only).</summary>
        public string? Password { get; set; } = null;
    }
}
