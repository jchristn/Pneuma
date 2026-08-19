namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// A single document hit from a subject-scoped Verbex search, linked back to the content link it was
    /// ingested from.
    /// </summary>
    public class SubjectSearchResult
    {
        /// <summary>Verbex document identifier.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Relevance score (higher is better).</summary>
        public double Score { get; set; } = 0;

        /// <summary>A text snippet of the matching document, when available.</summary>
        public string? Snippet { get; set; } = null;

        /// <summary>Identifier of the content link this document was ingested from, when resolvable.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>URL of the originating content link.</summary>
        public string? LinkUrl { get; set; } = null;

        /// <summary>Title of the originating content link.</summary>
        public string? LinkTitle { get; set; } = null;

        /// <summary>LiteGraph node id associated with the document, when present.</summary>
        public string? NodeId { get; set; } = null;
    }
}
