namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama <c>/api/chat</c> request.
    /// </summary>
    public class OllamaChatRequest
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
        /// Stream flag (the harness never streams; the stub honors it either way).
        /// </summary>
        [JsonPropertyName("stream")]
        public bool? Stream { get; set; } = null;

        /// <summary>
        /// Reasoning control: "false" disables it where supported; reasoning models that only take levels get "low",
        /// "medium", or "high".
        /// </summary>
        [JsonPropertyName("think")]
        [JsonConverter(typeof(ThinkConverter))]
        public string? Think { get; set; } = null;

        /// <summary>
        /// Generation options.
        /// </summary>
        [JsonPropertyName("options")]
        public OllamaChatOptions? Options { get; set; } = null;

        #endregion
    }
}
