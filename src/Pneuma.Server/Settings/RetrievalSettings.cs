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
        private int _NeighborExpansionMaxHops = 1;
        private int _CommunityMinSize = 3;
        private int _CommunitySummaryMaxMembers = 50;
        private int _CommunityDetectionMaxIterations = 100;
        private int _VectorTopK = 20;
        private double _VectorMinimumScore = 0.0;
        private int _ChatMaxToolIterations = 6;
        private int _RrfK = 60;
        private double _LexicalWeight = 1.0;
        private double _SemanticWeight = 1.0;
        private double _DiversityLambda = 0.7;
        private int _SearchPoolSize = 250;

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
        /// Maximum number of tool-calling iterations the agentic chat assistant may take before it is forced
        /// to produce a final answer. Higher values let the assistant gather more evidence at the cost of more
        /// model round-trips and latency. Default 6; minimum 1; maximum 20.
        /// </summary>
        public int ChatMaxToolIterations
        {
            get { return _ChatMaxToolIterations; }
            set { _ChatMaxToolIterations = Math.Clamp(value, 1, 20); }
        }

        /// <summary>
        /// Maximum number of neighbor nodes added during expansion. Default 10; minimum 1; maximum 200.
        /// </summary>
        public int NeighborExpansionMaxNodes
        {
            get { return _NeighborExpansionMaxNodes; }
            set { _NeighborExpansionMaxNodes = Math.Clamp(value, 1, 200); }
        }

        /// <summary>
        /// How many hops neighbor expansion traverses from each retrieved chunk's node. 1 (default) uses a
        /// direct-neighbor read; greater than 1 uses a bounded server-side subgraph extraction to reach
        /// entities several relationships away. Clamped to [1, 5].
        /// </summary>
        public int NeighborExpansionMaxHops
        {
            get { return _NeighborExpansionMaxHops; }
            set { _NeighborExpansionMaxHops = Math.Clamp(value, 1, 5); }
        }

        /// <summary>
        /// Minimum number of member entities a detected community must have to get its own community summary.
        /// Default 3; clamped to [2, 1000].
        /// </summary>
        public int CommunityMinSize
        {
            get { return _CommunityMinSize; }
            set { _CommunityMinSize = Math.Clamp(value, 2, 1000); }
        }

        /// <summary>
        /// Maximum member entities listed to the model when summarizing a community (bounds prompt size).
        /// Default 50; clamped to [5, 500].
        /// </summary>
        public int CommunitySummaryMaxMembers
        {
            get { return _CommunitySummaryMaxMembers; }
            set { _CommunitySummaryMaxMembers = Math.Clamp(value, 5, 500); }
        }

        /// <summary>Maximum iterations for community detection. Default 100; clamped to [1, 1000].</summary>
        public int CommunityDetectionMaxIterations
        {
            get { return _CommunityDetectionMaxIterations; }
            set { _CommunityDetectionMaxIterations = Math.Clamp(value, 1, 1000); }
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

        /// <summary>
        /// Rank-bias constant <c>k</c> in Reciprocal-Rank Fusion (RRF), which fuses the lexical and vector
        /// result lists by summing <c>weight / (k + rank)</c> across the channels a document appears in. Larger
        /// values flatten the contribution of top ranks (less aggressive fusion); smaller values sharpen it.
        /// Default 60 (the value from the original RRF paper); minimum 1; maximum 1000.
        /// </summary>
        public int RrfK
        {
            get { return _RrfK; }
            set { _RrfK = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>
        /// Weight applied to the lexical (full-text) channel's RRF contribution. Raising it above the semantic
        /// weight biases fusion toward keyword matches. Default 1.0; clamped to [0.0, 10.0].
        /// </summary>
        public double LexicalWeight
        {
            get { return _LexicalWeight; }
            set { _LexicalWeight = Math.Clamp(value, 0.0, 10.0); }
        }

        /// <summary>
        /// Weight applied to the semantic (vector) channel's RRF contribution. Raising it above the lexical
        /// weight biases fusion toward embedding similarity. Default 1.0; clamped to [0.0, 10.0].
        /// </summary>
        public double SemanticWeight
        {
            get { return _SemanticWeight; }
            set { _SemanticWeight = Math.Clamp(value, 0.0, 10.0); }
        }

        /// <summary>
        /// When true, retrieved passages are selected with Maximal Marginal Relevance so near-duplicate chunks
        /// do not crowd the grounding context; the answer sees diverse support rather than the same point
        /// repeated. Applies to grounded answering/chat retrieval, not the raw search endpoint. Default true.
        /// </summary>
        public bool DiversityEnabled { get; set; } = true;

        /// <summary>
        /// MMR trade-off between relevance and diversity: 1.0 is pure relevance (no diversity penalty), 0.0 is
        /// pure diversity. Default 0.7 (relevance-leaning); clamped to [0.0, 1.0].
        /// </summary>
        public double DiversityLambda
        {
            get { return _DiversityLambda; }
            set { _DiversityLambda = Math.Clamp(value, 0.0, 1.0); }
        }

        /// <summary>
        /// Full URL of a cross-encoder rerank endpoint (the standard <c>/rerank</c> contract). A global system
        /// setting; subjects opt in per-subject by selecting the cross-encoder reranker type. When empty,
        /// cross-encoder reranking is unavailable and subjects fall back to LLM listwise reranking.
        /// </summary>
        public string? CrossEncoderRerankUrl { get; set; } = null;

        /// <summary>Rerank model name sent to the cross-encoder endpoint (may be null).</summary>
        public string? CrossEncoderRerankModel { get; set; } = null;

        /// <summary>Bearer API key for the cross-encoder rerank endpoint (may be null).</summary>
        public string? CrossEncoderRerankApiKey { get; set; } = null;

        /// <summary>
        /// How many candidate chunks the subject-search endpoint over-fetches from the retrieval store before
        /// fusing the channels, rolling the hits up per source link, and paginating. A source link spans many
        /// chunks, so the pool must exceed the page size to cover enough distinct links; larger values improve
        /// link coverage and deep-pagination at the cost of more retrieval-store work and latency per query.
        /// Default 250; clamped to [20, 2000].
        /// </summary>
        public int SearchPoolSize
        {
            get { return _SearchPoolSize; }
            set { _SearchPoolSize = Math.Clamp(value, 20, 2000); }
        }

        #endregion
    }
}
