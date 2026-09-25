namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// An end-to-end answering benchmark report.
    /// </summary>
    public class AnswerReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind.
        /// </summary>
        public string Kind { get; set; } = "answer";

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// query (grounded, /v1.0/query), stream (/v1.0/query/stream), or chat (agentic, /v1.0/chat/stream).
        /// </summary>
        public string Endpoint { get; set; } = "query";

        /// <summary>
        /// Optional run label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Ingest summary.
        /// </summary>
        public IngestSummary Ingest { get; set; } = new IngestSummary();

        /// <summary>
        /// Headline metrics (means over all runs).
        /// </summary>
        public Dictionary<string, double> Summary { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Accuracy per repeat run, to show judge and generation variance.
        /// </summary>
        public List<double> AccuracyByRun { get; set; } = new List<double>();

        /// <summary>
        /// 95% bootstrap interval of accuracy.
        /// </summary>
        public ConfidenceInterval? AccuracyInterval { get; set; } = null;

        /// <summary>
        /// Metrics per query type.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByType { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stages during the run.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        /// <summary>
        /// Graded items.
        /// </summary>
        public List<AnswerItem> Items { get; set; } = new List<AnswerItem>();

        #endregion
    }
}
