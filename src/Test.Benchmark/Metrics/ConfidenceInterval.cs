namespace Test.Benchmark.Metrics
{
    /// <summary>
    /// A bootstrap confidence interval.
    /// </summary>
    public class ConfidenceInterval
    {
        #region Public-Members

        /// <summary>
        /// Lower bound.
        /// </summary>
        public double Low { get; set; } = 0.0;

        /// <summary>
        /// Upper bound.
        /// </summary>
        public double High { get; set; } = 0.0;

        #endregion

        #region Public-Methods

        /// <summary>
        /// True when the interval excludes zero (a significant difference, for a delta interval).
        /// </summary>
        /// <returns>True when both bounds share a sign.</returns>
        public bool ExcludesZero()
        {
            return (Low > 0.0 && High > 0.0) || (Low < 0.0 && High < 0.0);
        }

        #endregion
    }
}
