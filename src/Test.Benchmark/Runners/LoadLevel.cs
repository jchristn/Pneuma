namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// Load results at one concurrency level.
    /// </summary>
    public class LoadLevel
    {
        #region Public-Members

        /// <summary>
        /// Concurrent closed-loop workers.
        /// </summary>
        public int Concurrency { get; set; } = 1;

        /// <summary>
        /// Operations completed in the measured window.
        /// </summary>
        public long Operations { get; set; } = 0;

        /// <summary>
        /// Failed operations.
        /// </summary>
        public long Errors { get; set; } = 0;

        /// <summary>
        /// Operations per second.
        /// </summary>
        public double Throughput { get; set; } = 0.0;

        /// <summary>
        /// Error share.
        /// </summary>
        public double ErrorRate { get; set; } = 0.0;

        /// <summary>
        /// Client latency.
        /// </summary>
        public LatencyStats Latency { get; set; } = new LatencyStats();

        /// <summary>
        /// Server-side stages during the window.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        #endregion
    }
}
