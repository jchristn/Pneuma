namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// A single search result: a representative graph node with its relevance score.
    /// </summary>
    public class SearchNodeResult
    {
        /// <summary>The resolved graph node.</summary>
        public GraphNode Node { get; set; } = new GraphNode();

        /// <summary>Relevance score from the search index.</summary>
        public double Score { get; set; } = 0;

        /// <summary>A text snippet, when available.</summary>
        public string? Snippet { get; set; } = null;

        /// <summary>The originating content-link id, when known.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The retrieval-store document id of the hit.</summary>
        public string? DocumentId { get; set; } = null;

        /// <summary>
        /// Fused Reciprocal-Rank-Fusion score normalized to 0..1 (1 = ranked first by every channel that ran).
        /// Results are ordered by it, and unlike <see cref="Score"/> it is comparable across queries.
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

        /// <summary>"content" or "summary" (an LLM cell summary), when the chunk is tagged; null for older chunks.</summary>
        public string? ChunkKind { get; set; } = null;

        /// <summary>Stored position of the chunk within its source document.</summary>
        public int Position { get; set; } = 0;
    }
}
