namespace Test.Benchmark.Client
{
    /// <summary>
    /// One hit from <c>GET /v1.0/subjects/{id}/search</c>. The evidence fields (fused score, per-leg scores and
    /// ranks, chunk kind) are only present on builds that return them; they stay null otherwise.
    /// </summary>
    public class SubjectSearchHit
    {
        #region Public-Members

        /// <summary>
        /// Retrieval-store document id.
        /// </summary>
        public string? DocumentId { get; set; } = null;

        /// <summary>
        /// The score the server reports for the hit.
        /// </summary>
        public double Score { get; set; } = 0.0;

        /// <summary>
        /// Chunks of this document that matched.
        /// </summary>
        public int MatchCount { get; set; } = 0;

        /// <summary>
        /// Best chunk text.
        /// </summary>
        public string? Snippet { get; set; } = null;

        /// <summary>
        /// Originating link id (maps back to the benchmark document).
        /// </summary>
        public string? LinkId { get; set; } = null;

        /// <summary>
        /// Link URL.
        /// </summary>
        public string? LinkUrl { get; set; } = null;

        /// <summary>
        /// Graph node id.
        /// </summary>
        public string? NodeId { get; set; } = null;

        /// <summary>
        /// Normalized fused (RRF) score, 0..1, when returned.
        /// </summary>
        public double? FusedScore { get; set; } = null;

        /// <summary>
        /// Raw vector similarity, when the vector leg matched.
        /// </summary>
        public double? VectorScore { get; set; } = null;

        /// <summary>
        /// Raw full-text rank, when the text leg matched.
        /// </summary>
        public double? TextScore { get; set; } = null;

        /// <summary>
        /// 1-based rank in the vector leg.
        /// </summary>
        public int? VectorRank { get; set; } = null;

        /// <summary>
        /// 1-based rank in the text leg.
        /// </summary>
        public int? TextRank { get; set; } = null;

        /// <summary>
        /// content or summary, when returned.
        /// </summary>
        public string? ChunkKind { get; set; } = null;

        #endregion
    }
}
