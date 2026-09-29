namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>What a crawl run will do with one object, before anything is written.</summary>
    public class CrawlDeltaItem
    {
        #region Public-Members

        /// <summary>The object's key.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>The planned action.</summary>
        public CrawlActionEnum Action { get; set; } = CrawlActionEnum.Add;

        /// <summary>The enumerated object, or null when the object is gone from the source.</summary>
        public CrawledObject? Source { get; set; } = null;

        /// <summary>The baseline object from earlier runs, or null for a new object.</summary>
        public CrawlObject? Baseline { get; set; } = null;

        /// <summary>Why the object is skipped, or null.</summary>
        public string? Detail { get; set; } = null;

        #endregion
    }
}
