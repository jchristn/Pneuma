namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit multiple content links for a subject in a single call; enqueues one ingestion job per URL.
    /// </summary>
    public class BulkSubmitLinkRequest
    {
        /// <summary>The content URLs to ingest. The models and collection are taken from the subject.</summary>
        public List<string> Urls { get; set; } = new List<string>();
    }
}
