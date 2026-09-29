namespace Pneuma.Core.Crawling
{
    /// <summary>The outcome of asking a crawl plan to start or stop.</summary>
    public class CrawlStartResult
    {
        #region Public-Members

        /// <summary>The HTTP status the request maps to: 202 started or stopping, 404 unknown plan, 409 conflict, 400 invalid.</summary>
        public int StatusCode { get; set; } = 202;

        /// <summary>Why the request was refused, or null.</summary>
        public string? Error { get; set; } = null;

        /// <summary>The operation that started (or was stopped), or null.</summary>
        public CrawlOperation? Operation { get; set; } = null;

        #endregion
    }
}
