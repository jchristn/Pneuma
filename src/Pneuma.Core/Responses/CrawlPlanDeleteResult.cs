namespace Pneuma.Core.Responses
{
    /// <summary>The result of deleting a crawl plan.</summary>
    public class CrawlPlanDeleteResult
    {
        #region Public-Members

        /// <summary>True when the plan was deleted.</summary>
        public bool Deleted { get; set; } = false;

        /// <summary>Links marked for background deletion (with <c>deleteLinks=true</c>).</summary>
        public int LinksDeleted { get; set; } = 0;

        /// <summary>Links kept and detached from the plan (without <c>deleteLinks</c>).</summary>
        public int LinksKept { get; set; } = 0;

        #endregion
    }
}
