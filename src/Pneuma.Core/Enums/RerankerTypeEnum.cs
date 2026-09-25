namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Which reranking strategy a subject uses to reorder retrieved passages before answering.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RerankerTypeEnum
    {
        /// <summary>Listwise reranking by the subject's completion (LLM) model. The default.</summary>
        LlmListwise,

        /// <summary>
        /// A dedicated cross-encoder reranking service (a standard <c>/rerank</c> endpoint that scores each
        /// query/passage pair). Faster and cheaper per rerank than an LLM; falls back to the LLM listwise
        /// path when no cross-encoder is configured or the call fails.
        /// </summary>
        CrossEncoder
    }
}
