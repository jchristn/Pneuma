namespace Pneuma.Sdk.Responses
{
    /// <summary>The result of deleting a crawl plan.</summary>
    public class CrawlPlanDeleteResult
    {
        /// <summary>True when deleted.</summary>
        public bool Deleted { get; set; } = false;

        /// <summary>Links marked for deletion.</summary>
        public int LinksDeleted { get; set; } = 0;

        /// <summary>Links kept and detached.</summary>
        public int LinksKept { get; set; } = 0;
    }
}
