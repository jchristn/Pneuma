namespace Pneuma.Core.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// New subject wizard metrics, rendered as part of <see cref="PneumaMetrics.Render"/>:
    /// <c>pneuma_wizard_generation_total{step,outcome}</c> and <c>pneuma_wizard_generation_duration_seconds{step}</c>.
    /// </summary>
    public static class WizardMetrics
    {
        #region Private-Members

        private static readonly double[] _Buckets = new double[] { 0.5, 1, 2.5, 5, 10, 20, 30, 60, 120, 300 };
        private static readonly string[] _BucketLabels = BuildLabels();
        private static readonly ConcurrentDictionary<string, long> _Generations = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _Durations = new ConcurrentDictionary<string, MetricHistogram>();

        #endregion

        #region Public-Methods

        /// <summary>Record one wizard generation.</summary>
        /// <param name="step">brief, questions, ontology, prompts, or sources.</param>
        /// <param name="outcome">success, retried (succeeded on the second try), or failed.</param>
        /// <param name="seconds">Time the model call took.</param>
        public static void RecordGeneration(string step, string outcome, double seconds)
        {
            string safeStep = Escape(String.IsNullOrEmpty(step) ? "(unknown)" : step);
            string safeOutcome = Escape(String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome);
            _Generations.AddOrUpdate("step=\"" + safeStep + "\",outcome=\"" + safeOutcome + "\"", 1, (k, existing) => existing + 1);
            _Durations.GetOrAdd("step=\"" + safeStep + "\"", k => new MetricHistogram(_Buckets, _BucketLabels)).Observe(Math.Max(0, seconds));
        }

        /// <summary>Append the wizard metric families in Prometheus text format.</summary>
        /// <param name="sb">Target builder.</param>
        internal static void AppendTo(StringBuilder sb)
        {
            sb.Append("# HELP pneuma_wizard_generation_total New subject wizard generations, by step and outcome\n");
            sb.Append("# TYPE pneuma_wizard_generation_total counter\n");
            foreach (KeyValuePair<string, long> series in _Generations)
                sb.Append("pneuma_wizard_generation_total{").Append(series.Key).Append("} ").Append(series.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("# HELP pneuma_wizard_generation_duration_seconds New subject wizard model call duration in seconds, by step\n");
            sb.Append("# TYPE pneuma_wizard_generation_duration_seconds histogram\n");
            foreach (KeyValuePair<string, MetricHistogram> series in _Durations)
                series.Value.AppendTo(sb, "pneuma_wizard_generation_duration_seconds", series.Key);
        }

        #endregion

        #region Private-Methods

        private static string[] BuildLabels()
        {
            string[] labels = new string[_Buckets.Length];
            for (int i = 0; i < _Buckets.Length; i++) labels[i] = _Buckets[i].ToString(CultureInfo.InvariantCulture);
            return labels;
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }

        #endregion
    }
}
