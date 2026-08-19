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

        /// <summary>Chosen Partio embedding endpoint identifier (e.g. "default").</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Chosen Partio completion endpoint identifier (e.g. "default").</summary>
        public string? CompletionEndpointId { get; set; } = null;

        /// <summary>Chosen RecallDB collection identifier the ingested chunks are stored in and searched from.</summary>
        public string? CollectionId { get; set; } = null;
    }
}
