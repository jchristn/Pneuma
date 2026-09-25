namespace Test.Benchmark.Reference
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One line of the embedding cache file.
    /// </summary>
    public class CachedEmbedding
    {
        #region Public-Members

        /// <summary>
        /// SHA-256 of the embedded text.
        /// </summary>
        [JsonPropertyName("h")]
        public string Hash { get; set; } = string.Empty;

        /// <summary>
        /// The vector.
        /// </summary>
        [JsonPropertyName("v")]
        public float[] Vector { get; set; } = new float[0];

        #endregion
    }
}
