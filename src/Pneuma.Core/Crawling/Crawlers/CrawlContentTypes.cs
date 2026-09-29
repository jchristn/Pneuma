namespace Pneuma.Core.Crawling.Crawlers
{
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>Guesses a content type from a file name, for shares that report none.</summary>
    public static class CrawlContentTypes
    {
        #region Private-Members

        private static readonly Dictionary<string, string> _ByExtension = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".pdf", "application/pdf" },
            { ".doc", "application/msword" },
            { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { ".xls", "application/vnd.ms-excel" },
            { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { ".ppt", "application/vnd.ms-powerpoint" },
            { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
            { ".txt", "text/plain" },
            { ".md", "text/markdown" },
            { ".markdown", "text/markdown" },
            { ".htm", "text/html" },
            { ".html", "text/html" },
            { ".json", "application/json" },
            { ".csv", "text/csv" },
            { ".xml", "application/xml" },
            { ".rtf", "application/rtf" },
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" }
        };

        #endregion

        #region Public-Methods

        /// <summary>The content type for a file name's extension, or null when unknown.</summary>
        /// <param name="name">File name or key.</param>
        /// <returns>The content type, or null.</returns>
        public static string? FromName(string? name)
        {
            if (String.IsNullOrEmpty(name)) return null;
            string ext = Path.GetExtension(name);
            string? type;
            return !String.IsNullOrEmpty(ext) && _ByExtension.TryGetValue(ext, out type) ? type : null;
        }

        #endregion
    }
}
