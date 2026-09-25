namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// One question answered by Pneuma and graded.
    /// </summary>
    public class AnswerItem
    {
        #region Public-Members

        /// <summary>
        /// Repeat index (0-based).
        /// </summary>
        public int Run { get; set; } = 0;

        /// <summary>
        /// Corpus id.
        /// </summary>
        public string Corpus { get; set; } = string.Empty;

        /// <summary>
        /// Query id.
        /// </summary>
        public string QueryId { get; set; } = string.Empty;

        /// <summary>
        /// Query type.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Question.
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Gold answer.
        /// </summary>
        public string Gold { get; set; } = string.Empty;

        /// <summary>
        /// Pneuma's answer.
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// True when the corpus answers the question.
        /// </summary>
        public bool Answerable { get; set; } = true;

        /// <summary>
        /// Judge verdict (correct answer, or correct decline for an unanswerable question); null when unparsed.
        /// </summary>
        public bool? Correct { get; set; } = null;

        /// <summary>
        /// Relevant documents.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Documents whose content was sent to the model (query) or that were cited (chat).
        /// </summary>
        public List<string> SourceDocuments { get; set; } = new List<string>();

        /// <summary>
        /// Documents the answer cites.
        /// </summary>
        public List<string> CitedDocuments { get; set; } = new List<string>();

        /// <summary>
        /// True when a relevant document (or a gold evidence span) reached the model.
        /// </summary>
        public bool EvidenceInContext { get; set; } = false;

        /// <summary>
        /// Share of gold evidence spans present in the source text sent to the model.
        /// </summary>
        public double? EvidenceCoverage { get; set; } = null;

        /// <summary>
        /// Per-claim faithfulness, when measured.
        /// </summary>
        public double? Faithfulness { get; set; } = null;

        /// <summary>
        /// Server "grounded" flag (query endpoint).
        /// </summary>
        public bool? Grounded { get; set; } = null;

        /// <summary>
        /// Server insufficient-support flag, when returned.
        /// </summary>
        public bool? InsufficientSupport { get; set; } = null;

        /// <summary>
        /// Agentic tool calls made (chat endpoint).
        /// </summary>
        public int? ToolCalls { get; set; } = null;

        /// <summary>
        /// Client latency.
        /// </summary>
        public double LatencyMs { get; set; } = 0.0;

        /// <summary>
        /// HTTP status.
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Error text.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
