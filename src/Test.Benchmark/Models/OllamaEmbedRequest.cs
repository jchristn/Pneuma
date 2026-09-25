namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama <c>/api/embed</c> request (also accepted as <c>/api/embeddings</c> with <see cref="Prompt"/>).
    /// </summary>
    public class OllamaEmbedRequest
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Inputs (the harness always sends a list; the stub also accepts a single string via a custom read).
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(StringOrListConverter))]
        public List<string>? Input { get; set; } = null;

        /// <summary>
        /// Single input for the legacy <c>/api/embeddings</c> route.
        /// </summary>
        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; } = null;

        #endregion
    }
}
