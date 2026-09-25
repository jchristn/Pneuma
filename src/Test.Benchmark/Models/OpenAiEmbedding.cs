namespace Test.Benchmark.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One vector of an OpenAI embeddings response.
    /// </summary>
    public class OpenAiEmbedding
    {
        #region Public-Members

        /// <summary>
        /// Always "embedding".
        /// </summary>
        [JsonPropertyName("object")]
        public string Object { get; set; } = "embedding";

        /// <summary>
        /// Input index.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// The vector.
        /// </summary>
        [JsonPropertyName("embedding")]
        public float[] Embedding { get; set; } = new float[0];

        #endregion
    }
}
