namespace Pneuma.Core.Requests
{
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit multiple content links for ingestion in a single call.
    /// </summary>
    public class BulkSubmitLinkRequest
    {
        /// <summary>The content URLs to ingest.</summary>
        public List<string> Urls { get; set; } = new List<string>();
    }
}
