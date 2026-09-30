namespace Pneuma.Core.Observability
{
    using System;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// One bucketed histogram series for <see cref="PneumaMetrics"/>. Bucket counts are non-cumulative per index
    /// (rendered cumulatively) with a trailing +Inf bucket; sum and sample count are maintained alongside. Thread-safe.
    /// </summary>
    internal sealed class MetricHistogram
    {
        #region Private-Members

        private readonly double[] _Buckets;
        private readonly string[] _BucketLabels;
        private readonly long[] _Counts;
        private readonly object _Lock = new object();
        private long _SampleCount = 0;
        private double _Sum = 0.0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate.</summary>
        /// <param name="buckets">Upper bounds of the buckets, ascending (shared, not copied).</param>
        /// <param name="bucketLabels">The bounds formatted for the le label, one per bucket.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        internal MetricHistogram(double[] buckets, string[] bucketLabels)
        {
            _Buckets = buckets ?? throw new ArgumentNullException(nameof(buckets));
            _BucketLabels = bucketLabels ?? throw new ArgumentNullException(nameof(bucketLabels));
            _Counts = new long[_Buckets.Length + 1];
        }

        #endregion

        #region Public-Methods

        /// <summary>Observe a single sample value (seconds).</summary>
        /// <param name="value">Sample value.</param>
        public void Observe(double value)
        {
            int index = _Buckets.Length;
            for (int i = 0; i < _Buckets.Length; i++)
            {
                if (value <= _Buckets[i])
                {
                    index = i;
                    break;
                }
            }

            lock (_Lock)
            {
                _Counts[index] = _Counts[index] + 1L;
                _SampleCount = _SampleCount + 1L;
                _Sum = _Sum + value;
            }
        }

        /// <summary>Append this series to the exposition output.</summary>
        /// <param name="sb">Target builder.</param>
        /// <param name="name">Metric family name.</param>
        /// <param name="innerLabels">Escaped label content without surrounding braces.</param>
        public void AppendTo(StringBuilder sb, string name, string innerLabels)
        {
            long[] snapshot = new long[_Counts.Length];
            long sampleCount;
            double sum;
            lock (_Lock)
            {
                Array.Copy(_Counts, snapshot, _Counts.Length);
                sampleCount = _SampleCount;
                sum = _Sum;
            }

            long cumulative = 0;
            for (int i = 0; i < _Buckets.Length; i++)
            {
                cumulative = cumulative + snapshot[i];
                sb.Append(name).Append("_bucket{").Append(innerLabels).Append(",le=\"").Append(_BucketLabels[i]).Append("\"} ")
                  .Append(cumulative.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }

            cumulative = cumulative + snapshot[_Buckets.Length];
            sb.Append(name).Append("_bucket{").Append(innerLabels).Append(",le=\"+Inf\"} ")
              .Append(cumulative.ToString(CultureInfo.InvariantCulture)).Append('\n');

            sb.Append(name).Append("_sum{").Append(innerLabels).Append("} ").Append(sum.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append(name).Append("_count{").Append(innerLabels).Append("} ").Append(sampleCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        #endregion
    }
}
