namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Result of setting the scheduled refresh of several links.
    /// </summary>
    public class LinkRefreshBulkResult
    {
        /// <summary>Links updated.</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Ids that were not found or cannot be refreshed (pushed content, crawled, or being deleted).</summary>
        public List<string> Skipped { get; set; } = new List<string>();
    }
}
