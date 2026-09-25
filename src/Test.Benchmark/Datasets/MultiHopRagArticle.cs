namespace Test.Benchmark.Datasets
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// One news article in the MultiHop-RAG corpus.json.
    /// </summary>
    public class MultiHopRagArticle
    {
        #region Public-Members

        /// <summary>
        /// Article title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Author.
        /// </summary>
        [JsonPropertyName("author")]
        public string? Author { get; set; } = null;

        /// <summary>
        /// Publisher.
        /// </summary>
        [JsonPropertyName("source")]
        public string? Source { get; set; } = null;

        /// <summary>
        /// Publication time (ISO 8601).
        /// </summary>
        [JsonPropertyName("published_at")]
        public string? PublishedAt { get; set; } = null;

        /// <summary>
        /// News category.
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; } = null;

        /// <summary>
        /// Article URL (unique; used to match evidence).
        /// </summary>
        [JsonPropertyName("url")]
        public string? Url { get; set; } = null;

        /// <summary>
        /// Article body.
        /// </summary>
        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Evidence fact (evidence-list entries only).
        /// </summary>
        [JsonPropertyName("fact")]
        public string? Fact { get; set; } = null;

        #endregion
    }
}
