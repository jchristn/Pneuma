namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit multiple content links for a subject in a single call; enqueues one ingestion job per
    /// URL. Any supplied <see cref="Labels"/> and <see cref="Tags"/> are applied to every URL in the batch.
    /// </summary>
    public class BulkSubmitLinkRequest
    {
        /// <summary>The content URLs to ingest. The models and collection are taken from the subject.</summary>
        public List<string> Urls { get; set; } = new List<string>();

        /// <summary>Optional labels applied to every URL in the batch.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Optional key/value tags applied to every URL in the batch.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
