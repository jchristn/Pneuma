namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Request to submit a content link for ingestion.
    /// </summary>
    public class SubmitLinkRequest
    {
        /// <summary>The content URL to ingest.</summary>
        public string Url { get; set; } = String.Empty;

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;
    }
}
