namespace Pneuma.Sdk.Models
{
    /// <summary>Settings for a local folder crawl plan (a folder on the Pneuma server under an allowed root).</summary>
    public class LocalFolderCrawlSettings
    {
        /// <summary>Absolute folder path on the server.</summary>
        public string Folder { get; set; } = string.Empty;

        /// <summary>Include files in subfolders.</summary>
        public bool IncludeSubfolders { get; set; } = true;
    }
}
