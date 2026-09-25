namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// OpenAI <c>/v1/chat/completions</c> request.
    /// </summary>
    public class OpenAiChatRequest
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Messages.
        /// </summary>
        [JsonPropertyName("messages")]
        public List<ChatWireMessage> Messages { get; set; } = new List<ChatWireMessage>();

        /// <summary>
        /// Sampling temperature.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Maximum tokens.
        /// </summary>
        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; set; } = null;

        /// <summary>
        /// Stream flag.
        /// </summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; } = null;

        #endregion
    }
}
