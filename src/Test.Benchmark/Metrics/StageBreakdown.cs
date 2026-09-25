namespace Test.Benchmark.Metrics
{
    /// <summary>
    /// Server-side time spent in one stage over a benchmark phase, from Prometheus histogram deltas.
    /// </summary>
    public class StageBreakdown
    {
        #region Public-Members

        /// <summary>
        /// Observations during the phase.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Mean duration per observation.
        /// </summary>
        public double MeanMs { get; set; } = 0.0;

        /// <summary>
        /// Total duration.
        /// </summary>
        public double TotalMs { get; set; } = 0.0;

        #endregion
    }
}
