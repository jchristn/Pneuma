namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;

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
        /// Relevance score: the best raw channel score the hit received (cosine similarity from the vector
        /// channel, TsRank from the full-text channel). In hybrid mode results are ordered by
        /// <see cref="FusedScore"/>, not by this value; the two channels' raw scores are not on one scale.
        /// </summary>
        public double Score { get; set; } = 0;

        /// <summary>
        /// Fused Reciprocal-Rank-Fusion score normalized to 0..1, where 1 means ranked first by every channel that
        /// ran. Comparable across queries; results are ordered by it.
        /// </summary>
        public double FusedScore { get; set; } = 0;

        /// <summary>Raw cosine similarity from the vector channel, or null when that channel did not return the hit.</summary>
        public double? VectorScore { get; set; } = null;

        /// <summary>Raw TsRank from the full-text channel, or null when that channel did not return the hit.</summary>
        public double? TextScore { get; set; } = null;

        /// <summary>1-based rank in the vector channel, or null.</summary>
        public int? VectorRank { get; set; } = null;

        /// <summary>1-based rank in the full-text channel, or null.</summary>
        public int? TextRank { get; set; } = null;

        /// <summary>
        /// Whether the best-matching chunk is document content ("content") or an LLM cell summary ("summary"), when
        /// the chunk carries a <c>chunkKind</c> tag (chunks indexed before the tag existed report null).
        /// </summary>
        public string? ChunkKind { get; set; } = null;

        /// <summary>Stored position of the chunk within its source document.</summary>
        public int Position { get; set; } = 0;

        /// <summary>A text snippet for display, when available.</summary>
        public string? Snippet { get; set; } = null;

        /// <summary>The originating content-link id, when the hit carried one.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The retrieval-store document id of the hit.</summary>
        public string? DocumentId { get; set; } = null;
    }
}
