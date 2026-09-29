namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>One object in a crawl preview.</summary>
    public class CrawlPreviewItem
    {
        #region Public-Members

        /// <summary>The object's key.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>What a run would do with it.</summary>
        public CrawlActionEnum Action { get; set; } = CrawlActionEnum.Add;

        /// <summary>Content type, when known.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Size in bytes, or 0 when unknown.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>Why the object would be skipped, or null.</summary>
        public string? Detail { get; set; } = null;

        #endregion
    }
}
