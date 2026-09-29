namespace Pneuma.Core.Crawling
{
    /// <summary>Settings for a local folder crawl plan: a folder on the Pneuma server itself.</summary>
    public class LocalFolderCrawlSettings
    {
        #region Public-Members

        /// <summary>Absolute folder path on the server; must be under one of the server's allowed roots.</summary>
        [CrawlSetting("Folder", "Absolute path of a folder on the Pneuma server (for a container, a path inside it, such as a mounted volume). It must be under one of the roots an administrator allows in Crawling.AllowedLocalRoots.", Required = true)]
        public string Folder { get; set; } = string.Empty;

        /// <summary>Include files in subfolders. Default true.</summary>
        [CrawlSetting("Include subfolders", "Include files in subfolders.")]
        public bool IncludeSubfolders { get; set; } = true;

        #endregion
    }
}
