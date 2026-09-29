namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for an NFS crawl plan: an exported file system.</summary>
    public class NfsCrawlSettings
    {
        #region Public-Members

        /// <summary>NFS server host name or IP address.</summary>
        [CrawlSetting("Host", "NFS server host name or IP address. When Pneuma runs in a container, localhost is taken to mean the host machine.", Required = true)]
        public string Host { get; set; } = string.Empty;

        /// <summary>NFS port. Default 2049; 1 to 65535.</summary>
        [CrawlSetting("NFS port", "NFS service port. Default 2049.", Min = 1, Max = 65535)]
        public int Port { get; set; } = 2049;

        /// <summary>MOUNT service port. Default 20048; 1 to 65535.</summary>
        [CrawlSetting("Mount port", "MOUNT (mountd) service port. Default 20048, the usual fixed mountd port on Linux servers; 0 asks the server's portmapper (port 111).", Min = 0, Max = 65535)]
        public int MountPort { get; set; } = 20048;

        /// <summary>Export path on the server.</summary>
        [CrawlSetting("Export", "Export path on the server (for example /srv/docs).", Required = true)]
        public string Export { get; set; } = string.Empty;

        /// <summary>Only files whose paths start with this prefix.</summary>
        [CrawlSetting("Path", "Only crawl files under this folder of the export.")]
        public string? Path { get; set; } = null;

        /// <summary>Include files in subfolders. Default true.</summary>
        [CrawlSetting("Include subfolders", "Include files in subfolders.")]
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>NFS protocol version. V3 (the only version the crawler supports today).</summary>
        [CrawlSetting("Version", "NFS protocol version. The crawler supports NFSv3.", Options = new[] { "V3" })]
        public string Version { get; set; } = "V3";

        /// <summary>User id presented to the server (AUTH_SYS). Default 0; 0 to 2147483647.</summary>
        [CrawlSetting("User id", "Numeric user id presented to the server.", Min = 0, Max = 2147483647)]
        public int UserId { get; set; } = 0;

        /// <summary>Group id presented to the server (AUTH_SYS). Default 0; 0 to 2147483647.</summary>
        [CrawlSetting("Group id", "Numeric group id presented to the server.", Min = 0, Max = 2147483647)]
        public int GroupId { get; set; } = 0;

        #endregion
    }
}
