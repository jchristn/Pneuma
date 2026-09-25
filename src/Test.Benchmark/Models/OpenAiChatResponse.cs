namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// OpenAI <c>/v1/chat/completions</c> response (or one streamed chunk).
    /// </summary>
    public class OpenAiChatResponse
    {
        #region Public-Members

        /// <summary>
        /// Response id.
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = "stub";

        /// <summary>
        /// chat.completion or chat.completion.chunk.
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "chat.completion";

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = "stub";

        /// <summary>
        /// Choices.
        /// </summary>
        [JsonPropertyName("choices")]
        public List<OpenAiChoice> Choices { get; set; } = new List<OpenAiChoice>();

        #endregion
    }
}
