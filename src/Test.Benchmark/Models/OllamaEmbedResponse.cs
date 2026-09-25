namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama <c>/api/embed</c> response (and, with <see cref="Embedding"/>, the legacy <c>/api/embeddings</c> one).
    /// </summary>
    public class OllamaEmbedResponse
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string? Model { get; set; } = null;

        /// <summary>
        /// One vector per input.
        /// </summary>
        [JsonPropertyName("embeddings")]
        public List<float[]>? Embeddings { get; set; } = null;

        /// <summary>
        /// Single vector (legacy route).
        /// </summary>
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; } = null;

        #endregion
    }
}
