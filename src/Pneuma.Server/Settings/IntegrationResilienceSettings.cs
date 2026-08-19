namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Resilience settings applied to every outbound integration client (DocumentAtom, Partio, Verbex,
    /// LiteGraph): per-attempt timeout, per-service concurrency bulkhead, and transient-failure retry
    /// budget. Values are clamped to safe ranges. Writes never retry regardless of <see cref="RetryCount"/>.
    /// </summary>
    public class IntegrationResilienceSettings
    {
        #region Private-Members

        private int _TimeoutMilliseconds = 100000;
        private int _MaxConcurrentRequests = 8;
        private int _RetryCount = 2;
        private int _RetryDelayMilliseconds = 500;

        #endregion

        #region Public-Members

        /// <summary>
        /// Per-attempt request timeout in milliseconds. Default 100000; minimum 1000; maximum 600000.
        /// </summary>
        public int TimeoutMilliseconds
        {
            get { return _TimeoutMilliseconds; }
            set { _TimeoutMilliseconds = Math.Clamp(value, 1000, 600000); }
        }

        /// <summary>
        /// Maximum concurrent outbound requests to any one service. Default 8; minimum 1; maximum 1024.
        /// </summary>
        public int MaxConcurrentRequests
        {
            get { return _MaxConcurrentRequests; }
            set { _MaxConcurrentRequests = Math.Clamp(value, 1, 1024); }
        }

        /// <summary>
        /// Retry attempts for transient failures on non-write requests. Default 2; minimum 0; maximum 10.
        /// </summary>
        public int RetryCount
        {
            get { return _RetryCount; }
            set { _RetryCount = Math.Clamp(value, 0, 10); }
        }

        /// <summary>
        /// Fixed delay between retries in milliseconds. Default 500; minimum 50; maximum 30000.
        /// </summary>
        public int RetryDelayMilliseconds
        {
            get { return _RetryDelayMilliseconds; }
            set { _RetryDelayMilliseconds = Math.Clamp(value, 50, 30000); }
        }

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize integration resilience settings with defaults.</summary>
        public IntegrationResilienceSettings()
        {
        }

        #endregion
    }
}
