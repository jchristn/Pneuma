namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for an NFS export crawl plan.</summary>
    public class NfsCrawlSettings
    {
        /// <summary>Server host name or address.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>Export path.</summary>
        public string Export { get; set; } = string.Empty;

        /// <summary>NFS port (default 2049).</summary>
        public int Port { get; set; } = 2049;

        /// <summary>MOUNT port (default 20048).</summary>
        public int MountPort { get; set; } = 20048;

        /// <summary>Folder within the export.</summary>
        public string? Path { get; set; } = null;

        /// <summary>Include subfolders.</summary>
        public bool IncludeSubfolders { get; set; } = true;

        /// <summary>NFS version; V3 is supported.</summary>
        public string Version { get; set; } = "V3";

        /// <summary>UID presented to the server.</summary>
        public int UserId { get; set; } = 0;

        /// <summary>GID presented to the server.</summary>
        public int GroupId { get; set; } = 0;
    }
}
