namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Concurrency-control settings for outbound model-runner usage (the answering models that back the
    /// grounded query and agentic chat endpoints). A process-wide gate caps how many model-runner requests
    /// may execute at once; additional requests queue up to <see cref="MaxQueueDepth"/>, and callers beyond
    /// that are rejected with HTTP 429 so a burst cannot overwhelm the model runner. Values are clamped to
    /// safe ranges.
    /// </summary>
    public class ModelRunnerSettings
    {
        #region Private-Members

        private int _MaxConcurrentRequests = 4;
        private int _MaxQueueDepth = 16;

        #endregion

        #region Public-Members

        /// <summary>
        /// Maximum number of model-runner requests allowed to execute concurrently. Default 4; minimum 1;
        /// maximum 1024.
        /// </summary>
        public int MaxConcurrentRequests
        {
            get { return _MaxConcurrentRequests; }
            set { _MaxConcurrentRequests = Math.Clamp(value, 1, 1024); }
        }

        /// <summary>
        /// Maximum number of requests that may wait in the queue once the concurrency limit is reached.
        /// A request that arrives when the queue is already full is rejected with HTTP 429. Default 16;
        /// minimum 0 (no queueing — reject immediately when saturated); maximum 100000.
        /// </summary>
        public int MaxQueueDepth
        {
            get { return _MaxQueueDepth; }
            set { _MaxQueueDepth = Math.Clamp(value, 0, 100000); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize model-runner concurrency settings with defaults.</summary>
        public ModelRunnerSettings()
        {
        }

        #endregion
    }
}
