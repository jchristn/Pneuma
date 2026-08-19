namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>
    /// Request to submit a content link for ingestion.
    /// </summary>
    public class SubmitLinkRequest
    {
        /// <summary>The content URL to ingest.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Identifier of the Partio embedding endpoint to use for ingestion (prefix "eep_").</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Identifier of the Partio completion endpoint to use for ingestion (prefix "cep_").</summary>
        public string? CompletionEndpointId { get; set; } = null;
    }
}
