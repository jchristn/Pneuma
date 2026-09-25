namespace Test.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Latency distribution summary in milliseconds (nearest-rank percentiles).
    /// </summary>
    public class LatencyStats
    {
        #region Public-Members

        /// <summary>
        /// Samples.
        /// </summary>
        public int Count { get; set; } = 0;

        /// <summary>
        /// Mean.
        /// </summary>
        public double Mean { get; set; } = 0.0;

        /// <summary>
        /// Median.
        /// </summary>
        public double P50 { get; set; } = 0.0;

        /// <summary>
        /// 90th percentile.
        /// </summary>
        public double P90 { get; set; } = 0.0;

        /// <summary>
        /// 95th percentile.
        /// </summary>
        public double P95 { get; set; } = 0.0;

        /// <summary>
        /// 99th percentile.
        /// </summary>
        public double P99 { get; set; } = 0.0;

        /// <summary>
        /// Maximum.
        /// </summary>
        public double Max { get; set; } = 0.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Summarize samples.
        /// </summary>
        /// <param name="samples">Latency samples in milliseconds.</param>
        /// <returns>The summary.</returns>
        public static LatencyStats From(IEnumerable<double> samples)
        {
            List<double> sorted = samples.OrderBy(s => s).ToList();
            LatencyStats stats = new LatencyStats { Count = sorted.Count };
            if (sorted.Count == 0) return stats;

            stats.Mean = Math.Round(sorted.Average(), 2);
            stats.P50 = Percentile(sorted, 0.50);
            stats.P90 = Percentile(sorted, 0.90);
            stats.P95 = Percentile(sorted, 0.95);
            stats.P99 = Percentile(sorted, 0.99);
            stats.Max = Math.Round(sorted[sorted.Count - 1], 2);
            return stats;
        }

        #endregion

        #region Private-Methods

        private static double Percentile(List<double> sorted, double p)
        {
            int rank = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (rank < 0) rank = 0;
            if (rank >= sorted.Count) rank = sorted.Count - 1;
            return Math.Round(sorted[rank], 2);
        }

        #endregion
    }
}
