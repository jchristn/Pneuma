namespace Test.Benchmark.Models
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// OpenAI <c>/v1/embeddings</c> request.
    /// </summary>
    public class OpenAiEmbedRequest
    {
        #region Public-Members

        /// <summary>
        /// Model name.
        /// </summary>
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Inputs.
        /// </summary>
        [JsonPropertyName("input")]
        [JsonConverter(typeof(StringOrListConverter))]
        public List<string> Input { get; set; } = new List<string>();

        #endregion
    }
}
