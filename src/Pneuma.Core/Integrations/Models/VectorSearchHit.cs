namespace Pneuma.Core.Integrations.Models
{
    using Pneuma.Core.Graph;

    /// <summary>
    /// A single vector-search result: the matched graph node identifier, its similarity score, and the
    /// hydrated node when available.
    /// </summary>
    public class VectorSearchHit
    {
        #region Public-Members

        /// <summary>Identifier of the matched graph node.</summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>Similarity score (higher is closer); semantics depend on the configured metric.</summary>
        public double Score { get; set; }

        /// <summary>The hydrated node when the store returned it; otherwise null.</summary>
        public GraphNode? Node { get; set; }

        /// <summary>The matched chunk's stored text content, when the store returned it; otherwise null.</summary>
        public string? Content { get; set; } = null;

        /// <summary>Zero-based position of the chunk within its source document (for ordering/reconstruction).</summary>
        public int Position { get; set; } = 0;

        /// <summary>The originating content link's id (for citation back to the ingested source), when present.</summary>
        public string? LinkId { get; set; } = null;

        #endregion
    }
}
