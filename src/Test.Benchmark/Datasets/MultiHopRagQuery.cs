namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One query in MultiHopRAG.json.
    /// </summary>
    public class MultiHopRagQuery
    {
        #region Public-Members

        /// <summary>
        /// The question.
        /// </summary>
        [JsonPropertyName("query")]
        public string Query { get; set; } = string.Empty;

        /// <summary>
        /// Gold answer ("Insufficient information." for null queries).
        /// </summary>
        [JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// inference_query, comparison_query, temporal_query, or null_query.
        /// </summary>
        [JsonPropertyName("question_type")]
        public string QuestionType { get; set; } = string.Empty;

        /// <summary>
        /// Evidence facts with the article each came from.
        /// </summary>
        [JsonPropertyName("evidence_list")]
        public List<MultiHopRagArticle> EvidenceList { get; set; } = new List<MultiHopRagArticle>();

        #endregion
    }
}
