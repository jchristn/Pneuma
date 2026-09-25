namespace Test.Benchmark.Datasets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One line of a BEIR corpus.jsonl or queries.jsonl file.
    /// </summary>
    public class BeirRecord
    {
        #region Public-Members

        /// <summary>
        /// Record id.
        /// </summary>
        [JsonPropertyName("_id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Title (corpus records only; often empty).
        /// </summary>
        [JsonPropertyName("title")]
        public string? Title { get; set; } = null;

        /// <summary>
        /// Text.
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        #endregion
    }
}
