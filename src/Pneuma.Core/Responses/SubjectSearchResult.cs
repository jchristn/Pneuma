namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// A single search result for a subject-scoped RecallDB search, aggregated to one row per source document
    /// (content link). A source link is stored as many RecallDB chunk documents (one per chunk), so a result rolls
    /// its matching chunks up to the link: the score is the best matching chunk's score and
    /// <see cref="MatchCount"/> is how many chunks matched.
    /// </summary>
    public class SubjectSearchResult
    {
        /// <summary>RecallDB document identifier of the best-matching chunk.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Best relevance score across the source's matching chunks (higher is better).</summary>
        public double Score { get; set; } = 0;

        /// <summary>How many chunks (RecallDB documents) from this source matched the query.</summary>
        public int MatchCount { get; set; } = 0;

        /// <summary>A text snippet from the best-matching chunk, when available.</summary>
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
