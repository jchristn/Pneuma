namespace Pneuma.Core.Crawling
{
    using System;

    /// <summary>
    /// One stored crawl plan setting value: a scalar, or one element of a list (by ordinal). Rows are the persisted form
    /// of the plan's typed settings and filter lists (decision D1 in ADDING_CRAWLERS.md).
    /// </summary>
    public class CrawlPlanSetting
    {
        #region Public-Members

        /// <summary>Setting name (for example "startUrls" or "filter.includePatterns").</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Position within a list; 0 for a scalar.</summary>
        public int Ordinal { get; set; } = 0;

        /// <summary>The value as invariant text.</summary>
        public string Value { get; set; } = String.Empty;

        #endregion
    }
}
