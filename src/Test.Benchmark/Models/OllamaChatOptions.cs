namespace Test.Benchmark.Models
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Ollama generation options.
    /// </summary>
    public class OllamaChatOptions
    {
        #region Public-Members

        /// <summary>
        /// Sampling temperature.
        /// </summary>
        [JsonPropertyName("temperature")]
        public double? Temperature { get; set; } = null;

        /// <summary>
        /// Maximum tokens to generate.
        /// </summary>
        [JsonPropertyName("num_predict")]
        public int? NumPredict { get; set; } = null;

        #endregion
    }
}
