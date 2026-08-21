namespace Pneuma.Core.Responses
{
    /// <summary>
    /// Headline per-subject chat analytics over a time window: volume, latency percentiles, token averages,
    /// and feedback tallies.
    /// </summary>
    public class AnalyticsOverview
    {
        #region Public-Members

        /// <summary>Number of answered turns in the window.</summary>
        public int TurnCount { get; set; } = 0;

        /// <summary>Average generation time in milliseconds.</summary>
        public double AvgGenerationMs { get; set; } = 0;

        /// <summary>Median (p50) generation time in milliseconds.</summary>
        public double P50GenerationMs { get; set; } = 0;

        /// <summary>95th-percentile generation time in milliseconds.</summary>
        public double P95GenerationMs { get; set; } = 0;

        /// <summary>99th-percentile generation time in milliseconds.</summary>
        public double P99GenerationMs { get; set; } = 0;

        /// <summary>Average time to first token in milliseconds.</summary>
        public double AvgTimeToFirstTokenMs { get; set; } = 0;

        /// <summary>Average prompt tokens per turn.</summary>
        public double AvgPromptTokens { get; set; } = 0;

        /// <summary>Average completion tokens per turn.</summary>
        public double AvgCompletionTokens { get; set; } = 0;

        /// <summary>Average completion tokens per second over the generation window.</summary>
        public double AvgTokensPerSecond { get; set; } = 0;

        /// <summary>Count of thumbs-up feedback in the window.</summary>
        public int ThumbsUp { get; set; } = 0;

        /// <summary>Count of thumbs-down feedback in the window.</summary>
        public int ThumbsDown { get; set; } = 0;

        #endregion
    }
}
