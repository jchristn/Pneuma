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

        /// <summary>Optional operator-facing title. The models and collection used for ingestion are taken from
        /// the subject, so no model selection is supplied here.</summary>
        public string? Title { get; set; } = null;
    }
}
