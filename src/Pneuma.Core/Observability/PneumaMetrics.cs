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
        private static readonly ConcurrentDictionary<string, MetricHistogram> _HttpDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, long> _IngestionJobs = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionStages = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _IngestionStageDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, long> _IntegrationRequests = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _IntegrationDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, long> _AuthzDecisions = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _ChatAnswers = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _ChatAnswerDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _ChatStageDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _RetrievalStageDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, long> _RetrievalLegFailures = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionFailures = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionPartial = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _ModelRetries = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionRetired = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _IngestionRechunk = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _ModelLimiterWait = new ConcurrentDictionary<string, MetricHistogram>();
        private static readonly ConcurrentDictionary<string, long> _CrawlOperations = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _CrawlObjects = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _CrawlBytes = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, MetricHistogram> _CrawlDuration = new ConcurrentDictionary<string, MetricHistogram>();
        private static long _CrawlRunning = 0;
        private static readonly ConcurrentDictionary<string, long> _LinkRefresh = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _ClassificationCache = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _OntologyViolations = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _TaxonomyLinks = new ConcurrentDictionary<string, long>();
        private static readonly ConcurrentDictionary<string, long> _OntologyOperations = new ConcurrentDictionary<string, long>();

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

        /// <summary>Record a failed ingestion job by its failure category.</summary>
        /// <param name="category">The failure category name (for example Fetch, Storage, ModelUnavailable).</param>
        public static void RecordIngestionFailure(string category)
        {
            string safeCategory = String.IsNullOrEmpty(category) ? "(unknown)" : category;
            Increment(_IngestionFailures, "category=\"" + Escape(safeCategory) + "\"");
        }

        /// <summary>Record work an ingestion job dropped but completed without.</summary>
        /// <param name="stage">The stage that dropped the work.</param>
        /// <param name="reason">A low-cardinality reason: classification_batch, summary, cell_node.</param>
        public static void RecordIngestionPartial(string stage, string reason)
        {
            string safeStage = String.IsNullOrEmpty(stage) ? "(unknown)" : stage;
            string safeReason = String.IsNullOrEmpty(reason) ? "(unknown)" : reason;
            Increment(_IngestionPartial, "stage=\"" + Escape(safeStage) + "\",reason=\"" + Escape(safeReason) + "\"");
        }

        /// <summary>Record output removed from an earlier version of a link (or a failed attempt).</summary>
        /// <param name="kind">What was removed: chunk_set (one job's chunks) or node (a Source or Cell node).</param>
        /// <param name="count">How many.</param>
        public static void RecordIngestionRetired(string kind, int count)
        {
            string safeKind = String.IsNullOrEmpty(kind) ? "(unknown)" : kind;
            string key = "kind=\"" + Escape(safeKind) + "\"";
            long amount = Math.Max(0, count);
            _IngestionRetired.AddOrUpdate(key, amount, (k, existing) => existing + amount);
        }

        /// <summary>Record a finished crawl operation and its duration.</summary>
        /// <param name="type">Crawl plan type (Web, Sitemap, S3, Cifs, Nfs).</param>
        /// <param name="outcome">Final status (Succeeded, PartiallySucceeded, Failed, Cancelled, Held).</param>
        /// <param name="seconds">Duration from start to finish in seconds.</param>
        public static void RecordCrawlOperation(string type, string outcome, double seconds)
        {
            string safeType = String.IsNullOrEmpty(type) ? "(unknown)" : type;
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_CrawlOperations, "type=\"" + Escape(safeType) + "\",outcome=\"" + Escape(safeOutcome) + "\"");
            _CrawlDuration.GetOrAdd("type=\"" + Escape(safeType) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>Record what a crawl operation did with its objects.</summary>
        /// <param name="type">Crawl plan type.</param>
        /// <param name="action">Action (Add, Update, Retry, Delete, Skip, Fail, Unchanged).</param>
        /// <param name="count">How many objects.</param>
        public static void RecordCrawlObjects(string type, string action, int count)
        {
            if (count <= 0) return;
            string safeType = String.IsNullOrEmpty(type) ? "(unknown)" : type;
            string safeAction = String.IsNullOrEmpty(action) ? "(unknown)" : action;
            string key = "type=\"" + Escape(safeType) + "\",action=\"" + Escape(safeAction) + "\"";
            long amount = count;
            _CrawlObjects.AddOrUpdate(key, amount, (k, existing) => existing + amount);
        }

        /// <summary>Record bytes a crawl operation enumerated.</summary>
        /// <param name="type">Crawl plan type.</param>
        /// <param name="bytes">Total size of the enumerated objects.</param>
        public static void RecordCrawlBytes(string type, long bytes)
        {
            if (bytes <= 0) return;
            string safeType = String.IsNullOrEmpty(type) ? "(unknown)" : type;
            string key = "type=\"" + Escape(safeType) + "\"";
            _CrawlBytes.AddOrUpdate(key, bytes, (k, existing) => existing + bytes);
        }

        /// <summary>Change the number of crawl operations enumerating on this server.</summary>
        /// <param name="delta">+1 when one starts, -1 when it stops.</param>
        public static void AdjustCrawlRunning(int delta)
        {
            Interlocked.Add(ref _CrawlRunning, delta);
        }

        /// <summary>Record a scheduled link refresh check.</summary>
        /// <param name="outcome">Unchanged, Queued, Failed, Busy, or Skipped.</param>
        public static void RecordLinkRefresh(string outcome)
        {
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_LinkRefresh, "outcome=\"" + Escape(safeOutcome) + "\"");
        }

        /// <summary>Record a classification cache lookup.</summary>
        /// <param name="outcome">hit or miss.</param>
        public static void RecordClassificationCache(string outcome)
        {
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_ClassificationCache, "outcome=\"" + Escape(safeOutcome) + "\"");
        }

        /// <summary>Record ontology rule violations found in classified content or a stored graph.</summary>
        /// <param name="ruleType">The rule type, or Undeclared for an undeclared type.</param>
        /// <param name="action">The action applied.</param>
        /// <param name="count">How many.</param>
        public static void RecordOntologyViolations(string ruleType, string action, int count)
        {
            if (count <= 0) return;
            string safeType = String.IsNullOrEmpty(ruleType) ? "(unknown)" : ruleType;
            string safeAction = String.IsNullOrEmpty(action) ? "(unknown)" : action;
            Add(_OntologyViolations, "rule_type=\"" + Escape(safeType) + "\",action=\"" + Escape(safeAction) + "\"", count);
        }

        /// <summary>Record taxonomy links added to or removed from the graph.</summary>
        /// <param name="change">added or removed.</param>
        /// <param name="count">How many.</param>
        public static void RecordTaxonomyLinks(string change, int count)
        {
            if (count <= 0) return;
            string safeChange = String.IsNullOrEmpty(change) ? "(unknown)" : change;
            Add(_TaxonomyLinks, "change=\"" + Escape(safeChange) + "\"", count);
        }

        /// <summary>Record a finished ontology operation.</summary>
        /// <param name="kind">Validate, Retag, or DriftCheck.</param>
        /// <param name="outcome">Succeeded or Failed.</param>
        public static void RecordOntologyOperation(string kind, string outcome)
        {
            string safeKind = String.IsNullOrEmpty(kind) ? "(unknown)" : kind;
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_OntologyOperations, "kind=\"" + Escape(safeKind) + "\",outcome=\"" + Escape(safeOutcome) + "\"");
        }

        /// <summary>Record a chunk re-chunked at a smaller size after the embedding model rejected it as too long.</summary>
        /// <param name="scale">The size scale tried (0.75, 0.5, or 0.3).</param>
        public static void RecordIngestionRechunk(string scale)
        {
            string safeScale = String.IsNullOrEmpty(scale) ? "(unknown)" : scale;
            Increment(_IngestionRechunk, "scale=\"" + Escape(safeScale) + "\"");
        }

        /// <summary>Record a retried model-endpoint request.</summary>
        /// <param name="runner">The runner name.</param>
        /// <param name="status">The HTTP status that caused the retry.</param>
        public static void RecordModelRetry(string runner, string status)
        {
            string safeRunner = String.IsNullOrEmpty(runner) ? "(unknown)" : runner;
            string safeStatus = String.IsNullOrEmpty(status) ? "(unknown)" : status;
            Increment(_ModelRetries, "runner=\"" + Escape(safeRunner) + "\",status=\"" + Escape(safeStatus) + "\"");
        }

        /// <summary>Record how long a request waited for a free slot on a model endpoint.</summary>
        /// <param name="runner">The runner name.</param>
        /// <param name="seconds">The wait in seconds.</param>
        public static void RecordModelLimiterWait(string runner, double seconds)
        {
            string safeRunner = String.IsNullOrEmpty(runner) ? "(unknown)" : runner;
            _ModelLimiterWait.GetOrAdd("runner=\"" + Escape(safeRunner) + "\"", CreateHistogram).Observe(seconds);
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
        /// <param name="service">Service name: documentatom, recalldb, litegraph, or polyprompt.</param>
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

        /// <summary>Record a completed chat/answer turn's outcome and total duration.</summary>
        /// <param name="outcome">Answer outcome: ok, insufficient, error, or cancelled.</param>
        /// <param name="seconds">Total answer duration in seconds.</param>
        public static void RecordChatAnswer(string outcome, double seconds)
        {
            string safeOutcome = String.IsNullOrEmpty(outcome) ? "(unknown)" : outcome;
            Increment(_ChatAnswers, "outcome=\"" + Escape(safeOutcome) + "\"");
            _ChatAnswerDuration.GetOrAdd("outcome=\"" + Escape(safeOutcome) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>Record one answer-pipeline stage's duration (rewrite, retrieval, rerank, tool, inference).</summary>
        /// <param name="stage">Stage name.</param>
        /// <param name="seconds">Stage duration in seconds.</param>
        public static void RecordChatStage(string stage, double seconds)
        {
            string safeStage = String.IsNullOrEmpty(stage) ? "(unknown)" : stage;
            _ChatStageDuration.GetOrAdd("stage=\"" + Escape(safeStage) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>
        /// Record the duration of one retrieval / grounded-answer stage (for example text_leg, embed, vector_leg,
        /// fusion, mmr, neighbor_expand, rewrite, rerank, generate) on the search and query paths.
        /// </summary>
        /// <param name="stage">Stage name.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordRetrievalStage(string stage, double seconds)
        {
            string safeStage = String.IsNullOrEmpty(stage) ? "unknown" : stage;
            _RetrievalStageDuration.GetOrAdd("stage=\"" + Escape(safeStage) + "\"", CreateHistogram).Observe(seconds);
        }

        /// <summary>
        /// Count a retrieval leg that failed and was skipped (the request degraded to the remaining leg).
        /// </summary>
        /// <param name="leg">text or vector.</param>
        public static void RecordRetrievalLegFailure(string leg)
        {
            Increment(_RetrievalLegFailures, "leg=\"" + Escape(String.IsNullOrEmpty(leg) ? "unknown" : leg) + "\"");
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
            AppendCounterFamily(sb, "pneuma_ingestion_failures_total", "Failed ingestion jobs by failure category", _IngestionFailures);
            AppendCounterFamily(sb, "pneuma_ingestion_partial_total", "Work an ingestion job dropped but completed without, by stage and reason", _IngestionPartial);
            AppendCounterFamily(sb, "pneuma_ingestion_retired_total", "Output removed from earlier versions of links and from failed attempts, by kind", _IngestionRetired);
            AppendCounterFamily(sb, "pneuma_ingestion_rechunk_total", "Chunks re-chunked at a smaller size after the embedding model rejected them as too long, by scale", _IngestionRechunk);
            AppendCounterFamily(sb, "pneuma_model_retries_total", "Model-endpoint requests retried after a transient failure, by runner and status", _ModelRetries);
            AppendHistogramFamily(sb, "pneuma_model_limiter_wait_seconds", "Time a model request waited for a free slot on its endpoint, by runner", _ModelLimiterWait);
            AppendCounterFamily(sb, "pneuma_crawl_operations_total", "Finished crawl operations, by plan type and outcome", _CrawlOperations);
            AppendCounterFamily(sb, "pneuma_crawl_objects_total", "Objects crawl operations acted on, by plan type and action", _CrawlObjects);
            AppendHistogramFamily(sb, "pneuma_crawl_operation_duration_seconds", "Crawl operation duration from start to finish in seconds, by plan type", _CrawlDuration);
            AppendCounterFamily(sb, "pneuma_crawl_bytes_total", "Bytes of objects crawl operations enumerated, by plan type", _CrawlBytes);
            AppendGauge(sb, "pneuma_crawl_running", "Crawl operations enumerating on this server", Interlocked.Read(ref _CrawlRunning));
            AppendCounterFamily(sb, "pneuma_link_refresh_total", "Scheduled link refresh checks, by outcome", _LinkRefresh);
            AppendCounterFamily(sb, "pneuma_classification_cache_total", "Classification cache lookups, by outcome (hit or miss)", _ClassificationCache);
            AppendCounterFamily(sb, "pneuma_ontology_violations_total", "Ontology rule violations found, by rule type and action", _OntologyViolations);
            AppendCounterFamily(sb, "pneuma_taxonomy_links_total", "Taxonomy links (cell ABOUT concept) added to or removed from the graph", _TaxonomyLinks);
            WizardMetrics.AppendTo(sb);
            AppendCounterFamily(sb, "pneuma_ontology_operations_total", "Finished ontology operations, by kind and outcome", _OntologyOperations);

            AppendCounterFamily(sb, "pneuma_integration_requests_total", "Integration requests by service, operation, and outcome", _IntegrationRequests);
            AppendHistogramFamily(sb, "pneuma_integration_request_duration_seconds", "Integration request duration in seconds, by service and operation", _IntegrationDuration);

            AppendCounterFamily(sb, "pneuma_authz_decisions_total", "Authorization decisions by result", _AuthzDecisions);

            AppendSimpleCounter(sb, "pneuma_http_requests_2xx_total", "HTTP 2xx/3xx responses", Interlocked.Read(ref _Requests2xx));
            AppendSimpleCounter(sb, "pneuma_http_requests_4xx_total", "HTTP 4xx responses", Interlocked.Read(ref _Requests4xx));
            AppendSimpleCounter(sb, "pneuma_http_requests_5xx_total", "HTTP 5xx responses", Interlocked.Read(ref _Requests5xx));
            AppendSimpleCounter(sb, "pneuma_ingestion_completed_total", "Completed ingestion jobs", Interlocked.Read(ref _IngestionCompleted));
            AppendSimpleCounter(sb, "pneuma_ingestion_failed_total", "Failed ingestion jobs", Interlocked.Read(ref _IngestionFailed));
            AppendCounterFamily(sb, "pneuma_chat_answers_total", "Chat/answer turns by outcome", _ChatAnswers);
            AppendHistogramFamily(sb, "pneuma_chat_answer_duration_seconds", "Chat answer total duration in seconds, by outcome", _ChatAnswerDuration);
            AppendHistogramFamily(sb, "pneuma_chat_stage_duration_seconds", "Chat answer-pipeline stage duration in seconds, by stage", _ChatStageDuration);
            AppendHistogramFamily(sb, "pneuma_retrieval_stage_duration_seconds", "Search and grounded-answer stage duration in seconds, by stage", _RetrievalStageDuration);
            AppendCounterFamily(sb, "pneuma_retrieval_leg_failures_total", "Retrieval legs that failed and were skipped, by leg", _RetrievalLegFailures);
            AppendSimpleCounter(sb, "pneuma_authz_denied_total", "Authorization denials", Interlocked.Read(ref _AuthzDenied));

            return sb.ToString();
        }

        #endregion

        #region Private-Methods

        private static MetricHistogram CreateHistogram(string key)
        {
            return new MetricHistogram(_Buckets, _BucketLabels);
        }

        private static void Increment(ConcurrentDictionary<string, long> family, string labelKey)
        {
            family.AddOrUpdate(labelKey, 1L, IncrementExisting);
        }

        private static void Add(ConcurrentDictionary<string, long> family, string labelKey, int count)
        {
            long amount = Math.Max(0, count);
            family.AddOrUpdate(labelKey, amount, (k, existing) => existing + amount);
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

        private static void AppendHistogramFamily(StringBuilder sb, string name, string help, ConcurrentDictionary<string, MetricHistogram> family)
        {
            sb.Append("# HELP ").Append(name).Append(' ').Append(help).Append('\n');
            sb.Append("# TYPE ").Append(name).Append(" histogram\n");
            foreach (System.Collections.Generic.KeyValuePair<string, MetricHistogram> series in family)
            {
                series.Value.AppendTo(sb, name, series.Key);
            }
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
    }
}
