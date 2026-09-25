namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// OpenAI <c>/v1/embeddings</c> response.
    /// </summary>
    public class OpenAiEmbedResponse
    {
        #region Public-Members

        /// <summary>
        /// Always "list".
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "list";

        /// <summary>
        /// Vectors.
        /// </summary>
        [JsonPropertyName("data")]
        public List<OpenAiEmbedding> Data { get; set; } = new List<OpenAiEmbedding>();

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = "stub";

        #endregion
    }
}
