namespace Pneuma.Core.Observability
{
    using System;
    using System.Collections.Concurrent;
    using System.Globalization;
    using System.Text;
    using System.Threading;

    /// <summary>
    /// Dependency-free, thread-safe, in-process Prometheus text-format metrics registry for Pneuma.
    /// Exposes typed recording methods for HTTP requests, ingestion jobs and stages, integration
    /// calls, and authorization decisions, plus a <see cref="Render"/> method returning the current
    /// state as Prometheus exposition text. Labeled series are stored in
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> instances keyed by a composed, escaped label
    /// string; histograms are bucketed counters. Metric families and label keys are stable public
    /// contract consumed by external dashboards.
    /// </summary>
    public static class PneumaMetrics
    {
        #region Private-Members

        private static readonly DateTime _StartUtc = DateTime.UtcNow;

        private static readonly double[] _Buckets = new double[]
        {
            0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600
        };

        private static readonly string[] _BucketLabels = BuildBucketLabels();

        private static readonly ConcurrentDictionary<string, long> _HttpRequests = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, HistogramSeries> _HttpDuration = new ConcurrentDictionary<string, HistogramSeries>();
        private static readonly ConcurrentDictionary<string, long> _IngestionJobs = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionStages = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, HistogramSeries> _IngestionStageDuration = new ConcurrentDictionary<string, HistogramSeries>();
        private static readonly ConcurrentDictionary<string, long> _IntegrationRequests = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, HistogramSeries> _IntegrationDuration = new ConcurrentDictionary<string, HistogramSeries>();
        private static readonly ConcurrentDictionary<string, long> _AuthzDecisions = new ConcurrentDictionary<string, long>();

        private static long _Requests2xx = 0;
        private static long _Requests4xx = 0;
        private static long _Requests5xx = 0;
        private static long _IngestionCompleted = 0;
        private static long _IngestionFailed = 0;
        private static long _AuthzDenied = 0;

        #endregion

        #region Public-Methods

        /// <summary>Record a completed HTTP request with method, normalized route, status code, and duration.</summary>
        /// <param name="method">HTTP method (e.g. GET, POST).</param>
        /// <param name="route">Normalized (low-cardinality) route.</param>
        /// <param name="statusCode">Response status code.</param>
        /// <param name="seconds">Request duration in seconds.</param>
        public static void RecordHttpRequest(string method, string route, int statusCode, double seconds)
        {
            string safeMethod = String.IsNullOrEmpty(method) ? "(unknown)" : method;
            string safeRoute = String.IsNullOrEmpty(route) ? "(unknown)" : route;
            string statusClass = StatusClass(statusCode);

            string counterKey = "method=\"" + Escape(safeMethod) + "\",route=\"" + Escape(safeRoute) + "\",status=\"" + statusClass + "\"";
            Increment(_HttpRequests, counterKey);

            string histoKey = "method=\"" + Escape(safeMethod) + "\",route=\"" + Escape(safeRoute) + "\"";
            _HttpDuration.GetOrAdd(histoKey, CreateHistogram).Observe(seconds);

            RecordStatusClassLegacy(statusCode);
        }

        /// <summary>Record an ingestion job outcome.</summary>
        /// <param name="outcome">Job outcome: started, completed, failed, or cancelled.</param>
        public static void RecordIngestionJob(string outcome)
        {
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_IngestionJobs, "outcome=\"" + Escape(safeOutcome) + "\"");
        }

        /// <summary>Record an ingestion pipeline stage result and duration.</summary>
        /// <param name="stage">Pipeline stage name.</param>
        /// <param name="outcome">Stage outcome: ok or failed.</param>
        /// <param name="seconds">Stage duration in seconds.</param>
        public static void RecordIngestionStage(string stage, string outcome, double seconds)
        {
            string safeStage = String.IsNullOrEmpty(stage) ? "(unknown)" : stage;
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;

            Increment(_IngestionStages, "stage=\"" + Escape(safeStage) + "\",outcome=\"" + Escape(safeOutcome) + "\"");
            _IngestionStageDuration.GetOrAdd("stage=\"" + Escape(safeStage) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>Record an integration (downstream service) request result and duration.</summary>
        /// <param name="service">Service name: documentatom, partio, recalldb, litegraph, or polyprompt.</param>
        /// <param name="operation">Low-cardinality operation label (normalized path).</param>
        /// <param name="outcome">Outcome: ok or error.</param>
        /// <param name="seconds">Request duration in seconds.</param>
        public static void RecordIntegration(string service, string operation, string outcome, double seconds)
        {
            string safeService = String.IsNullOrEmpty(service) ? "(unknown)" : service;
            string safeOperation = String.IsNullOrEmpty(operation) ? "(unknown)" : operation;
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;

            Increment(_IntegrationRequests, "service=\"" + Escape(safeService) + "\",operation=\"" + Escape(safeOperation) + "\",outcome=\"" + Escape(safeOutcome) + "\"");
            _IntegrationDuration.GetOrAdd("service=\"" + Escape(safeService) + "\",operation=\"" + Escape(safeOperation) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>Record an authorization decision.</summary>
        /// <param name="result">Decision result: permit or deny.</param>
        public static void RecordAuthzDecision(string result)
        {
            string safeResult = String.IsNullOrEmpty(result) ? "(unknown)" : result;
            Increment(_AuthzDecisions, "result=\"" + Escape(safeResult) + "\"");
        }

        /// <summary>Record a completed HTTP request by its status code only (back-compat; updates legacy status-class counters).</summary>
        /// <param name="statusCode">Response status code.</param>
        public static void RecordRequest(int statusCode)
        {
            RecordStatusClassLegacy(statusCode);
        }

        /// <summary>Record a completed ingestion job (back-compat legacy counter).</summary>
        public static void RecordIngestionCompleted() => Interlocked.Increment(ref _IngestionCompleted);

        /// <summary>Record a failed ingestion job (back-compat legacy counter).</summary>
        public static void RecordIngestionFailed() => Interlocked.Increment(ref _IngestionFailed);

        /// <summary>Record an authorization denial (back-compat legacy counter).</summary>
        public static void RecordAuthzDenied() => Interlocked.Increment(ref _AuthzDenied);

        /// <summary>Render the current metrics in Prometheus text exposition format.</summary>
        /// <returns>Prometheus-formatted metrics.</returns>
        public static string Render()
        {
            StringBuilder sb = new StringBuilder();

            AppendGauge(sb, "pneuma_uptime_seconds", "Server uptime in seconds", (DateTime.UtcNow - _StartUtc).TotalSeconds);

            AppendCounterFamily(sb, "pneuma_http_requests_total", "Total HTTP requests handled, by method, route, and status class", _HttpRequests);
            AppendHistogramFamily(sb, "pneuma_http_request_duration_seconds", "HTTP request duration in seconds, by method and route", _HttpDuration);

            AppendCounterFamily(sb, "pneuma_ingestion_jobs_total", "Ingestion jobs by outcome", _IngestionJobs);
            AppendCounterFamily(sb, "pneuma_ingestion_stage_total", "Ingestion pipeline stages by stage and outcome", _IngestionStages);
            AppendHistogramFamily(sb, "pneuma_ingestion_stage_duration_seconds", "Ingestion stage duration in seconds, by stage", _IngestionStageDuration);

            AppendCounterFamily(sb, "pneuma_integration_requests_total", "Integration requests by service, operation, and outcome", _IntegrationRequests);
            AppendHistogramFamily(sb, "pneuma_integration_request_duration_seconds", "Integration request duration in seconds, by service and operation", _IntegrationDuration);

            AppendCounterFamily(sb, "pneuma_authz_decisions_total", "Authorization decisions by result", _AuthzDecisions);

            AppendSimpleCounter(sb, "pneuma_http_requests_2xx_total", "HTTP 2xx/3xx responses", Interlocked.Read(ref _Requests2xx));
            AppendSimpleCounter(sb, "pneuma_http_requests_4xx_total", "HTTP 4xx responses", Interlocked.Read(ref _Requests4xx));
            AppendSimpleCounter(sb, "pneuma_http_requests_5xx_total", "HTTP 5xx responses", Interlocked.Read(ref _Requests5xx));
            AppendSimpleCounter(sb, "pneuma_ingestion_completed_total", "Completed ingestion jobs", Interlocked.Read(ref _IngestionCompleted));
            AppendSimpleCounter(sb, "pneuma_ingestion_failed_total", "Failed ingestion jobs", Interlocked.Read(ref _IngestionFailed));
            AppendSimpleCounter(sb, "pneuma_authz_denied_total", "Authorization denials", Interlocked.Read(ref _AuthzDenied));

            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static HistogramSeries CreateHistogram(string key)
        {
            return new HistogramSeries();
        }

        private static void Increment(ConcurrentDictionary<string, long> family, string labelKey)
        {
            family.AddOrUpdate(labelKey, 1L, IncrementExisting);
        }

        private static long IncrementExisting(string key, long existing)
        {
            return existing + 1L;
        }

        private static void RecordStatusClassLegacy(int statusCode)
        {
            if (statusCode >= 200 && statusCode < 400) Interlocked.Increment(ref _Requests2xx);
            else if (statusCode >= 400 && statusCode < 500) Interlocked.Increment(ref _Requests4xx);
            else if (statusCode >= 500) Interlocked.Increment(ref _Requests5xx);
        }

        private static string StatusClass(int statusCode)
        {
            if (statusCode >= 500) return "5xx";
            if (statusCode >= 400) return "4xx";
            if (statusCode >= 300) return "3xx";
            return "2xx";
        }

        private static string[] BuildBucketLabels()
        {
            string[] labels = new string[_Buckets.Length];
            for (int i = 0; i < _Buckets.Length; i++)
            {
                labels[i] = _Buckets[i].ToString(CultureInfo.InvariantCulture);
            }
            return labels;
        }

        private static void AppendGauge(StringBuilder sb, string name, string help, double value)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" gauge\n");
            sb.Append(name).Append(' ').Append(value.ToString("F0", CultureInfo.InvariantCulture)).Append('\n');
        }

        private static void AppendSimpleCounter(StringBuilder sb, string name, string help, long value)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" counter\n");
            sb.Append(name).Append(' ').Append(value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        private static void AppendCounterFamily(StringBuilder sb, string name, string help, ConcurrentDictionary<string, long> family)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" counter\n");
            foreach (System.Collections.Generic.KeyValuePair<string, long> series in family)
            {
                sb.Append(name).Append('{').Append(series.Key).Append("} ").Append(series.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        private static void AppendHistogramFamily(StringBuilder sb, string name, string help, ConcurrentDictionary<string, HistogramSeries> family)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" histogram\n");
            foreach (System.Collections.Generic.KeyValuePair<string, HistogramSeries> series in family)
            {
                series.Value.AppendTo(sb, name, series.Key);
            }
        }

        private static string FormatDouble(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            StringBuilder sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (c == '\\') sb.Append("\\\\");
                else if (c == '"') sb.Append("\\\"");
                else if (c == '\n') sb.Append("\\n");
                else sb.Append(c);
            }
            return sb.ToString();
        }

        #endregion

        #region Nested-Types

        /// <summary>
        /// A single bucketed histogram series. Bucket counts are non-cumulative per index (rendered
        /// cumulatively) with a trailing +Inf bucket; sum and sample count are maintained alongside.
        /// </summary>
        private sealed class HistogramSeries
        {
            private readonly long[] _Counts = new long[_Buckets.Length + 1];
            private readonly object _Lock = new object();
            private long _SampleCount = 0;
            private double _Sum = 0.0;

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

                sb.Append(name).Append("_sum{").Append(innerLabels).Append("} ").Append(FormatDouble(sum)).Append('\n');
                sb.Append(name).Append("_count{").Append(innerLabels).Append("} ").Append(sampleCount.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        #endregion
    }
}
