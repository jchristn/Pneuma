namespace Pneuma.Server.Settings
{
    using System;
    using Pneuma.Core.Integrations.Implementations;

    /// <summary>
    /// Ingestion worker settings.
    /// </summary>
    public class IngestionSettings
    {
        #region Public-Members

        /// <summary>Number of ingestion jobs that may process concurrently.</summary>
        public int MaxConcurrentTasks
        {
            get { return _MaxConcurrentTasks; }
            set { _MaxConcurrentTasks = Math.Clamp(value, 1, 64); }
        }

        /// <summary>Polling interval in milliseconds when the queue is empty.</summary>
        public int PollIntervalMs
        {
            get { return _PollIntervalMs; }
            set { _PollIntervalMs = Math.Clamp(value, 100, 60000); }
        }

        /// <summary>Maximum processing attempts before a job is left failed.</summary>
        public int MaxAttempts
        {
            get { return _MaxAttempts; }
            set { _MaxAttempts = Math.Clamp(value, 1, 10); }
        }

        /// <summary>
        /// Base backoff, in milliseconds, before retrying a job after a transient failure. The wait grows
        /// exponentially with the attempt number (base, base·2, base·4, …) and is capped at
        /// <see cref="RetryBackoffMaxMs"/>. Default 2000; clamped to [0, 300000].
        /// </summary>
        public int RetryBackoffBaseMs
        {
            get { return _RetryBackoffBaseMs; }
            set { _RetryBackoffBaseMs = Math.Clamp(value, 0, 300000); }
        }

        /// <summary>Maximum backoff, in milliseconds, between retry attempts. Default 60000; clamped to [0, 600000].</summary>
        public int RetryBackoffMaxMs
        {
            get { return _RetryBackoffMaxMs; }
            set { _RetryBackoffMaxMs = Math.Clamp(value, 0, 600000); }
        }

        /// <summary>
        /// Maximum number of embedding vectors held in the in-memory embedding cache (a global system limit
        /// shared across all subjects and tenants). Identical text is served from the cache instead of being
        /// re-embedded, saving the embedding round-trip on re-ingestion and duplicate content. Set to 0 to
        /// disable caching. Default 50000; clamped to [0, 5000000].
        /// </summary>
        public int EmbeddingCacheSize
        {
            get { return _EmbeddingCacheSize; }
            set { _EmbeddingCacheSize = Math.Clamp(value, 0, 5000000); }
        }

        /// <summary>
        /// Ceiling, in seconds, for a single pipeline stage before it is cancelled and the job is retried.
        /// Applies to every stage including the model-runner-bound ones (classification, summarization,
        /// embedding), which can legitimately run long on large documents; keep it generous so a long but
        /// progressing stage is not repeatedly cancelled into a full-job retry. Default 900; clamped to [5, 3600].
        /// </summary>
        public int StageTimeoutSeconds
        {
            get { return _StageTimeoutSeconds; }
            set { _StageTimeoutSeconds = Math.Clamp(value, 5, 3600); }
        }

        /// <summary>
        /// Minimum trimmed length, in characters, a cell must have to be summarized. Short fragments (headings,
        /// captions, single list items) are skipped so summarization does not fire a model call per trivial cell
        /// on large documents. Default 128; clamped to [0, 100000] (0 summarizes every non-empty cell).
        /// </summary>
        public int SummarizationMinCellLength
        {
            get { return _SummarizationMinCellLength; }
            set { _SummarizationMinCellLength = Math.Clamp(value, 0, 100000); }
        }

        /// <summary>
        /// Maximum number of cells a single job summarizes concurrently. Bounds the model calls one large
        /// document can issue at once so summarization completes in bounded time instead of hundreds of serial
        /// calls. Default 4; clamped to [1, 64].
        /// </summary>
        public int SummarizationConcurrency
        {
            get { return _SummarizationConcurrency; }
            set { _SummarizationConcurrency = Math.Clamp(value, 1, 64); }
        }

        /// <summary>
        /// Number of cells classified per model call. A document with more cells than this is split into batches
        /// that are classified independently and merged, so no single classification call carries an unbounded
        /// prompt that a slow model cannot finish in time. Default 25; clamped to [1, 100000].
        /// </summary>
        public int ClassificationBatchSize
        {
            get { return _ClassificationBatchSize; }
            set { _ClassificationBatchSize = Math.Clamp(value, 1, 100000); }
        }

        /// <summary>
        /// Number of context cells included on each side of a classification batch (read symmetrically before and
        /// after the batch's own cells) so a relationship whose endpoints straddle a batch boundary is still seen
        /// from at least one batch. Default 3; clamped to [0, 1000].
        /// </summary>
        public int ClassificationBatchOverlap
        {
            get { return _ClassificationBatchOverlap; }
            set { _ClassificationBatchOverlap = Math.Clamp(value, 0, 1000); }
        }

        /// <summary>
        /// Maximum classification batches a single job runs concurrently. Bounds the model calls one large document
        /// issues at once so classification completes in bounded time. Default 4; clamped to [1, 64].
        /// </summary>
        public int ClassificationBatchConcurrency
        {
            get { return _ClassificationBatchConcurrency; }
            set { _ClassificationBatchConcurrency = Math.Clamp(value, 1, 64); }
        }

        /// <summary>
        /// When true, source links are crawled with a headless Chromium browser (Playwright) so that
        /// JavaScript-rendered pages are captured; falls back to a plain HTTP fetch on failure.
        /// </summary>
        public bool UseHeadlessBrowser { get; set; } = true;

        /// <summary>Headless-browser navigation timeout in milliseconds.</summary>
        public int BrowserNavigationTimeoutMs
        {
            get { return _BrowserNavigationTimeoutMs; }
            set { _BrowserNavigationTimeoutMs = Math.Clamp(value, 1000, 300000); }
        }

        /// <summary>
        /// User-Agent header presented when retrieving source content (both the HTTP and headless-browser
        /// fetchers). Defaults to a realistic modern desktop-browser string so bot-protected or
        /// User-Agent-gated sites serve their full content; setting it to null or empty restores the default.
        /// </summary>
        public string UserAgent
        {
            get { return _UserAgent; }
            set { _UserAgent = String.IsNullOrWhiteSpace(value) ? HttpContentFetcher.DefaultUserAgent : value; }
        }

        /// <summary>
        /// Per-stage concurrency caps. Bound how many jobs may run a given pipeline step at once (independent
        /// of <see cref="MaxConcurrentTasks"/>), so a large enqueue cannot overwhelm the model runners.
        /// </summary>
        public IngestionStageConcurrencySettings StageConcurrency { get; set; } = new IngestionStageConcurrencySettings();

        #endregion

        #region Private-Members

        private int _MaxConcurrentTasks = 4;
        private int _PollIntervalMs = 2000;
        private int _MaxAttempts = 3;
        private int _RetryBackoffBaseMs = 2000;
        private int _RetryBackoffMaxMs = 60000;
        private int _EmbeddingCacheSize = 50000;
        private int _StageTimeoutSeconds = 900;
        private int _SummarizationMinCellLength = 128;
        private int _SummarizationConcurrency = 4;
        private int _ClassificationBatchSize = 25;
        private int _ClassificationBatchOverlap = 3;
        private int _ClassificationBatchConcurrency = 4;
        private int _BrowserNavigationTimeoutMs = 60000;
        private string _UserAgent = HttpContentFetcher.DefaultUserAgent;

        #endregion
    }
}
