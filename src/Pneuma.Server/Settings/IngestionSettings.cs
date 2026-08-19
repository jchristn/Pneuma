namespace Pneuma.Server.Settings
{
    using System;

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

        /// <summary>Per-stage timeout in seconds.</summary>
        public int StageTimeoutSeconds
        {
            get { return _StageTimeoutSeconds; }
            set { _StageTimeoutSeconds = Math.Clamp(value, 5, 3600); }
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

        #endregion

        #region Private-Members

        private int _MaxConcurrentTasks = 4;
        private int _PollIntervalMs = 2000;
        private int _MaxAttempts = 3;
        private int _StageTimeoutSeconds = 300;
        private int _BrowserNavigationTimeoutMs = 60000;

        #endregion
    }
}
