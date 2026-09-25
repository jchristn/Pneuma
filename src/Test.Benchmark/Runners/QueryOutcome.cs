namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// One query run in one mode, with its ranking and per-query metrics.
    /// </summary>
    public class QueryOutcome
    {
        #region Public-Members

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
        /// Mode (text, vector, hybrid, ref-bm25, ref-dense, ref-hybrid).
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// HTTP status (200 for reference modes).
        /// </summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>
        /// Client latency.
        /// </summary>
        public double LatencyMs { get; set; } = 0.0;

        /// <summary>
        /// Ranked document ids.
        /// </summary>
        public List<string> Ranked { get; set; } = new List<string>();

        /// <summary>
        /// Relevant document ids.
        /// </summary>
        public List<string> Relevant { get; set; } = new List<string>();

        /// <summary>
        /// Metric name to value (answerable queries only).
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Score of the top hit as the server reports it.
        /// </summary>
        public double? TopScore { get; set; } = null;

        /// <summary>
        /// Highest raw vector similarity among the hits, when returned.
        /// </summary>
        public double? TopVectorScore { get; set; } = null;

        /// <summary>
        /// Normalized fused score of the top hit, when returned.
        /// </summary>
        public double? TopFusedScore { get; set; } = null;

        /// <summary>
        /// Summary chunks among the top 10 (chunk-granularity hits with a chunk kind only).
        /// </summary>
        public int? SummaryHits { get; set; } = null;

        /// <summary>
        /// Chunk-level hits among the top 10 (for the summary share denominator).
        /// </summary>
        public int? ChunkHits { get; set; } = null;

        /// <summary>
        /// Error text for a failed request.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
