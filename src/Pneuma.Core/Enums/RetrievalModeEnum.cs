namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which retrieval channels a query uses over the RecallDB collection.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
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
