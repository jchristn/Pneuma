namespace Pneuma.Core.Requests
{
    /// <summary>
    /// Per-request overrides of the global retrieval settings, for tuning and benchmarking without a restart
    /// (system or tenant administrators only; ignored for other callers). Every field is optional: a null field
    /// keeps the configured value. Accepted as <c>overrides</c> on grounded-query bodies and as query-string
    /// parameters of the same names on the search routes.
    /// </summary>
    public class RetrievalOverrides
    {
        #region Public-Members

        /// <summary>Reciprocal-Rank-Fusion constant k (1..1000).</summary>
        public int? RrfK { get; set; } = null;

        /// <summary>Weight of the full-text channel in fusion (0..10).</summary>
        public double? LexicalWeight { get; set; } = null;

        /// <summary>Weight of the vector channel in fusion (0..10).</summary>
        public double? SemanticWeight { get; set; } = null;

        /// <summary>Whether grounded answers select passages with Maximal Marginal Relevance.</summary>
        public bool? DiversityEnabled { get; set; } = null;

        /// <summary>MMR relevance-vs-novelty balance (0..1).</summary>
        public double? DiversityLambda { get; set; } = null;

        /// <summary>Grounded-answer candidate pool as a multiple of the requested source count (1..25).</summary>
        public int? PoolMultiplier { get; set; } = null;

        /// <summary>Whether grounded answers add graph neighbors of the retrieved passages.</summary>
        public bool? NeighborExpansionEnabled { get; set; } = null;

        /// <summary>Neighbor-expansion depth in hops (1..5).</summary>
        public int? NeighborExpansionMaxHops { get; set; } = null;

        /// <summary>Maximum neighbor nodes added (0..200).</summary>
        public int? NeighborExpansionMaxNodes { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when no field is set.
        /// </summary>
        /// <returns>True when empty.</returns>
        public bool IsEmpty()
        {
            return RrfK == null && LexicalWeight == null && SemanticWeight == null && DiversityEnabled == null && DiversityLambda == null
                && PoolMultiplier == null && NeighborExpansionEnabled == null && NeighborExpansionMaxHops == null && NeighborExpansionMaxNodes == null;
        }

        #endregion
    }
}
