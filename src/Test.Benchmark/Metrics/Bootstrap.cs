namespace Test.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Percentile bootstrap over per-query values, with a fixed seed so reports are reproducible. Used for the
    /// confidence interval of a mean metric and for the paired interval of a difference between two runs.
    /// </summary>
    public static class Bootstrap
    {
        #region Public-Members

        /// <summary>
        /// Default resamples.
        /// </summary>
        public const int DefaultResamples = 10000;

        /// <summary>
        /// Default seed.
        /// </summary>
        public const int DefaultSeed = 17;

        #endregion

        #region Public-Methods

        /// <summary>
        /// 95% interval of the mean of per-query values.
        /// </summary>
        /// <param name="values">Per-query values.</param>
        /// <param name="resamples">Resamples.</param>
        /// <param name="seed">Seed.</param>
        /// <returns>The interval, or null with fewer than two values.</returns>
        public static ConfidenceInterval? MeanInterval(IReadOnlyList<double> values, int resamples = DefaultResamples, int seed = DefaultSeed)
        {
            if (values == null || values.Count < 2) return null;
            Random random = new Random(seed);
            double[] means = new double[resamples];
            int n = values.Count;
            for (int r = 0; r < resamples; r++)
            {
                double sum = 0.0;
                for (int i = 0; i < n; i++) sum += values[random.Next(n)];
                means[r] = sum / n;
            }

            Array.Sort(means);
            return new ConfidenceInterval
            {
                Low = Math.Round(means[(int)Math.Floor(0.025 * (resamples - 1))], 4),
                High = Math.Round(means[(int)Math.Ceiling(0.975 * (resamples - 1))], 4)
            };
        }

        /// <summary>
        /// 95% paired interval of the mean difference (candidate minus baseline) over matched queries.
        /// </summary>
        /// <param name="baseline">Baseline per-query values.</param>
        /// <param name="candidate">Candidate per-query values, index-aligned with the baseline.</param>
        /// <param name="resamples">Resamples.</param>
        /// <param name="seed">Seed.</param>
        /// <returns>The interval, or null with fewer than two pairs.</returns>
        /// <exception cref="ArgumentException">Thrown when the lists differ in length.</exception>
        public static ConfidenceInterval? PairedDifference(IReadOnlyList<double> baseline, IReadOnlyList<double> candidate, int resamples = DefaultResamples, int seed = DefaultSeed)
        {
            if (baseline.Count != candidate.Count) throw new ArgumentException("Paired lists must be the same length.");
            List<double> deltas = new List<double>(baseline.Count);
            for (int i = 0; i < baseline.Count; i++) deltas.Add(candidate[i] - baseline[i]);
            return MeanInterval(deltas, resamples, seed);
        }

        #endregion
    }
}
