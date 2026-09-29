namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>The result of setting the refresh interval of several links.</summary>
    public class LinkRefreshBulkResult
    {
        #region Public-Members

        /// <summary>Links changed.</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Ids not changed: unknown, not a URL link, owned by a crawl plan, or being deleted.</summary>
        public List<string> Skipped { get; set; } = new List<string>();

        #endregion
    }
}
