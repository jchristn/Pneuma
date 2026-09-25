namespace Test.Benchmark.Metrics
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Client;

    /// <summary>
    /// A scrape of Pneuma's <c>/metrics</c>. Two snapshots taken around a benchmark phase give the server-side time
    /// spent per stage during that phase (histogram <c>_sum</c> and <c>_count</c> deltas), which is compared with
    /// client latency to see where the time actually goes. Each label set is kept separately, so for example the
    /// RecallDB search and the embedding call show up as distinct integration stages.
    /// </summary>
    public class PrometheusSnapshot
    {
        #region Public-Members

        /// <summary>
        /// Histogram families reported as stages.
        /// </summary>
        public static readonly string[] StageFamilies = new string[]
        {
            "pneuma_retrieval_stage_duration_seconds",
            "pneuma_integration_request_duration_seconds",
            "pneuma_chat_stage_duration_seconds",
            "pneuma_chat_answer_duration_seconds",
            "pneuma_ingestion_stage_duration_seconds",
            "pneuma_http_request_duration_seconds"
        };

        /// <summary>
        /// True when the scrape succeeded.
        /// </summary>
        public bool Available { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly Dictionary<string, double> _Values = new Dictionary<string, double>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Scrape the server.
        /// </summary>
        /// <param name="client">Pneuma client.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The snapshot (unavailable when the scrape failed).</returns>
        public static async Task<PrometheusSnapshot> CaptureAsync(PneumaClient client, CancellationToken token)
        {
            PrometheusSnapshot snapshot = new PrometheusSnapshot();
            string? text = await client.GetMetricsTextAsync(token).ConfigureAwait(false);
            if (text == null) return snapshot;
            snapshot.Parse(text);
            snapshot.Available = true;
            return snapshot;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Per-stage breakdown of what happened between an earlier snapshot and this one.
        /// </summary>
        /// <param name="before">The earlier snapshot.</param>
        /// <returns>Stage key (family short name plus labels) to breakdown.</returns>
        public Dictionary<string, StageBreakdown> Since(PrometheusSnapshot before)
        {
            Dictionary<string, StageBreakdown> stages = new Dictionary<string, StageBreakdown>(StringComparer.Ordinal);
            if (!Available || before == null || !before.Available) return stages;

            foreach (KeyValuePair<string, double> entry in _Values)
            {
                string key = entry.Key;
                int brace = key.IndexOf('{');
                string name = brace >= 0 ? key.Substring(0, brace) : key;
                if (!name.EndsWith("_count", StringComparison.Ordinal)) continue;
                string family = name.Substring(0, name.Length - 6);
                if (Array.IndexOf(StageFamilies, family) < 0) continue;
                string labels = brace >= 0 ? key.Substring(brace) : string.Empty;
                string sumKey = family + "_sum" + labels;

                double count = entry.Value - before.Value(key);
                double sum = Value(sumKey) - before.Value(sumKey);
                if (count <= 0) continue;
                string shortName = family.Replace("pneuma_", string.Empty).Replace("_duration_seconds", string.Empty) + labels;
                stages[shortName] = new StageBreakdown
                {
                    Count = (long)Math.Round(count),
                    MeanMs = Math.Round(sum * 1000.0 / count, 2),
                    TotalMs = Math.Round(sum * 1000.0, 1)
                };
            }

            return stages;
        }

        #endregion

        #region Private-Methods

        private double Value(string key)
        {
            return _Values.TryGetValue(key, out double value) ? value : 0.0;
        }

        private void Parse(string text)
        {
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                if (!line.StartsWith("pneuma_", StringComparison.Ordinal)) continue;

                // "name{labels} value [timestamp]" or "name value [timestamp]". Labels never contain a space
                // followed by a digit-leading token in Pneuma's metrics, so split on the closing brace.
                string key;
                string rest;
                int close = line.IndexOf('}');
                if (close > 0)
                {
                    key = line.Substring(0, close + 1);
                    rest = line.Substring(close + 1).Trim();
                }
                else
                {
                    int space = line.IndexOf(' ');
                    if (space <= 0) continue;
                    key = line.Substring(0, space);
                    rest = line.Substring(space + 1).Trim();
                }

                int end = rest.IndexOf(' ');
                string number = end > 0 ? rest.Substring(0, end) : rest;
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) continue;
                _Values[key] = value;
            }
        }

        #endregion
    }
}
