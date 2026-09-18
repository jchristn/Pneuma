namespace Pneuma.Core.Enums
{
    /// <summary>
    /// Which retrieval channels a query uses over the RecallDB collection.
    /// </summary>
    public enum RetrievalModeEnum
    {
        /// <summary>Lexical full-text (TsRank) search only.</summary>
        FullText,

        /// <summary>Semantic vector (cosine-similarity) search only.</summary>
        Vector,

        /// <summary>Both channels, fused with Reciprocal-Rank Fusion. The default.</summary>
        Hybrid
    }
}
