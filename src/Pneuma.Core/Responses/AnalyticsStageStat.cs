namespace Pneuma.Core.Responses
{
    /// <summary>
    /// Aggregated latency for one answer-pipeline stage across a window: how often it ran and its average and
    /// 95th-percentile duration.
    /// </summary>
    public class AnalyticsStageStat
    {
        #region Public-Members

        /// <summary>Stage name (for example "final_inference", "rerank", "tool:pneuma_search").</summary>
        public string Stage { get; set; } = string.Empty;

        /// <summary>Number of times the stage ran in the window.</summary>
        public int Count { get; set; } = 0;

        /// <summary>Average duration in milliseconds.</summary>
        public double AvgDurationMs { get; set; } = 0;

        /// <summary>95th-percentile duration in milliseconds.</summary>
        public double P95DurationMs { get; set; } = 0;

        #endregion
    }
}
