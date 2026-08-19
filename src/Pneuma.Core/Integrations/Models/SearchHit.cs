namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A single lexical (full-text) search hit from the chunk store: the stored document id, its relevance
    /// score, the tags stored with it (including <c>litegraphNodeId</c> for round-trip to the graph), and a
    /// text snippet.
    /// </summary>
    public class SearchHit
    {
        /// <summary>Store document identifier of the matching chunk.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Relevance score (higher is better).</summary>
        public double Score { get; set; } = 0;

        /// <summary>Tags stored with the chunk (includes <c>litegraphNodeId</c>, <c>linkId</c>, <c>subjectId</c>, <c>jobId</c>).</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>A text snippet of the matching chunk, when available.</summary>
        public string? Snippet { get; set; } = null;
    }
}
