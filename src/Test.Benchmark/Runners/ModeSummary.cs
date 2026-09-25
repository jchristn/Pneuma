namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Aggregate retrieval results for one mode.
    /// </summary>
    public class ModeSummary
    {
        #region Public-Members

        /// <summary>
        /// Mode.
        /// </summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>
        /// Answerable queries scored.
        /// </summary>
        public int Queries { get; set; } = 0;

        /// <summary>
        /// Unanswerable queries run.
        /// </summary>
        public int NegativeQueries { get; set; } = 0;

        /// <summary>
        /// Failed requests.
        /// </summary>
        public int Errors { get; set; } = 0;

        /// <summary>
        /// Mean metrics over answerable queries.
        /// </summary>
        public Dictionary<string, double> Metrics { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// 95% bootstrap interval of each mean metric.
        /// </summary>
        public Dictionary<string, ConfidenceInterval> Intervals { get; set; } = new Dictionary<string, ConfidenceInterval>();

        /// <summary>
        /// Mean metrics per query type (with a "count" entry).
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByType { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// AUROC of the reported top score as an answerable-vs-unanswerable classifier.
        /// </summary>
        public double? ScoreAuroc { get; set; } = null;

        /// <summary>
        /// AUROC of the top raw vector similarity, when returned.
        /// </summary>
        public double? VectorScoreAuroc { get; set; } = null;

        /// <summary>
        /// AUROC of the top normalized fused score, when returned.
        /// </summary>
        public double? FusedScoreAuroc { get; set; } = null;

        /// <summary>
        /// Mean top score for answerable queries.
        /// </summary>
        public double? MeanTopScoreAnswerable { get; set; } = null;

        /// <summary>
        /// Mean top score for unanswerable queries.
        /// </summary>
        public double? MeanTopScoreNegative { get; set; } = null;

        /// <summary>
        /// Share of top-10 chunk hits that are summary chunks, when chunk kinds are returned.
        /// </summary>
        public double? SummaryShare { get; set; } = null;

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stage breakdown for this mode.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        #endregion
    }
}
