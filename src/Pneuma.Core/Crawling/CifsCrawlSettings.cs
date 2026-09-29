namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for a CIFS/SMB crawl plan: a Windows or Samba file share.</summary>
    public class CifsCrawlSettings
    {
        #region Public-Members

        /// <summary>File server host name or IP address. Inside a container, localhost means the host machine.</summary>
        [CrawlSetting("Host", "File server host name or IP address. When Pneuma runs in a container, localhost is taken to mean the host machine.", Required = true)]
        public string Host { get; set; } = string.Empty;

        /// <summary>SMB port. Default 445; 1 to 65535.</summary>
        [CrawlSetting("Port", "SMB port. Default 445.", Min = 1, Max = 65535)]
        public int Port { get; set; } = 445;

        /// <summary>Share name.</summary>
        [CrawlSetting("Share", "Share name (for example Documents).", Required = true)]
        public string Share { get; set; } = string.Empty;

        /// <summary>Only files whose paths start with this prefix.</summary>
        [CrawlSetting("Path", "Only crawl files under this folder of the share (for example Policies/).")]
        public string? Path { get; set; } = null;

        /// <summary>Include files in subfolders. Default true.</summary>
        [CrawlSetting("Include subfolders", "Include files in subfolders.")]
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>Domain for the user, when the share is in a domain.</summary>
        [CrawlSetting("Domain", "Windows domain or workgroup of the account (for example CONTOSO or WORKGROUP). Some servers refuse a sign-in without it.")]
        public string? Domain { get; set; } = null;

        /// <summary>User name.</summary>
        [CrawlSetting("User name", "User name with read access to the share.", Required = true)]
        public string? Username { get; set; } = null;

        /// <summary>Password. Secret.</summary>
        [CrawlSetting("Password", "Password. Stored encrypted and never returned.", Secret = true)]
        public string? Password { get; set; } = null;

        #endregion
    }
}
