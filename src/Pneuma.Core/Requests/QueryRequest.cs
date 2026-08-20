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
    }
}
