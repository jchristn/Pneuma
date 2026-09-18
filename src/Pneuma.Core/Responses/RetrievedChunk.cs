namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Graph;

    /// <summary>
    /// A single scored retrieval hit returned by the search API: the resolved graph node the chunk points at,
    /// its relevance score, and the provenance needed to roll hits up per source document. Produced by the
    /// shared retrieval service so the full-text, vector, and hybrid search modes return one shape.
    /// </summary>
    public class RetrievedChunk
    {
        /// <summary>The knowledge-graph node the retrieved chunk resolves to (its originating cell or source).</summary>
        public GraphNode Node { get; set; } = new GraphNode();

        /// <summary>The resolved graph node id.</summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>
        /// Relevance score. For a single-channel mode this is the raw channel score (cosine similarity or
        /// TsRank); for hybrid it is the fused Reciprocal-Rank-Fusion score.
        /// </summary>
        public double Score { get; set; } = 0;

        /// <summary>A text snippet for display, when available.</summary>
        public string? Snippet { get; set; } = null;

        /// <summary>The originating content-link id, when the hit carried one.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The retrieval-store document id of the hit.</summary>
        public string? DocumentId { get; set; } = null;
    }
}
