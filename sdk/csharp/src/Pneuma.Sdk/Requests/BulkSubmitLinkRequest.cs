namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Request to submit multiple content links for a subject in a single call; enqueues one ingestion job per URL.
    /// </summary>
    public class BulkSubmitLinkRequest
    {
        /// <summary>The content URLs to ingest.</summary>
        public List<string> Urls { get; set; } = new List<string>();

        /// <summary>Identifier of the Partio embedding endpoint to use for ingestion (prefix "eep_").</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Identifier of the Partio completion endpoint to use for ingestion (prefix "cep_").</summary>
        public string? CompletionEndpointId { get; set; } = null;
    }
}
