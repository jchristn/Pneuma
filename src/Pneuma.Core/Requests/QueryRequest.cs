namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// A grounded question against a subject's corpus.
    /// </summary>
    public class QueryRequest
    {
        /// <summary>The natural-language question.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>Maximum sources to retrieve.</summary>
        public int MaxResults { get; set; } = 8;

        /// <summary>
        /// Optional subject to scope retrieval to. When set, only documents belonging to this subject are
        /// considered. Null answers over the whole tenant.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Optional per-request facet filter. Merged with the subject's default filter (union of required and
        /// excluded), so a request narrows — never widens — the subject default.
        /// </summary>
        public RetrievalFilter? MetadataFilter { get; set; } = null;
    }
}
