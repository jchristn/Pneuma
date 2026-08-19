namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Retrieval settings controlling how search resolves and enriches results. Governs whether the
    /// external full-text index (RecallDB) is used, whether graph-neighbor expansion enriches the hit set,
    /// and the vector-search parameters used once semantic vectors live in the graph store.
    /// </summary>
    public class RetrievalSettings
    {
        #region Private-Members

        private int _NeighborExpansionMaxNodes = 10;
        private int _VectorTopK = 20;
        private double _VectorMinimumScore = 0.0;

        #endregion

        #region Public-Members

        /// <summary>
        /// When true, the lexical (full-text) search path over the RecallDB collection is used in addition
        /// to vector search. Default true.
        /// </summary>
        public bool UseInvertedIndex { get; set; } = true;

        /// <summary>
        /// Default RecallDB collection id used by grounded query / chat when no collection is otherwise
        /// resolved. When empty, the tenant's first active collection is used.
        /// </summary>
        public string? DefaultCollectionId { get; set; } = null;

        /// <summary>
        /// When true, primary search hits are enriched with their graph neighbors (bounded by
        /// <see cref="NeighborExpansionMaxNodes"/>). Default false.
        /// </summary>
        public bool NeighborExpansionEnabled { get; set; } = false;

        /// <summary>
        /// Maximum number of neighbor nodes added during expansion. Default 10; minimum 1; maximum 200.
        /// </summary>
        public int NeighborExpansionMaxNodes
        {
            get { return _NeighborExpansionMaxNodes; }
            set { _NeighborExpansionMaxNodes = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Number of nearest vectors requested from the vector store. Default 20; minimum 1; maximum 200.
        /// </summary>
        public int VectorTopK
        {
            get { return _VectorTopK; }
            set { _VectorTopK = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// Minimum cosine similarity a vector hit must meet. Default 0.0; clamped to [0.0, 1.0].
        /// </summary>
        public double VectorMinimumScore
        {
            get { return _VectorMinimumScore; }
            set { _VectorMinimumScore = Math.Clamp(value, 0.0, 1.0); }
        }

        #endregion
    }
}
