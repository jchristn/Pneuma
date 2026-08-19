namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Request history capture settings.
    /// </summary>
    public class RequestHistorySettings
    {
        #region Public-Members

        /// <summary>Whether request capture is enabled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Maximum request body bytes captured before truncation.</summary>
        public int MaxRequestBodyBytes
        {
            get { return _MaxRequestBodyBytes; }
            set { _MaxRequestBodyBytes = Math.Clamp(value, 0, 1024 * 1024); }
        }

        /// <summary>Maximum response body bytes captured before truncation.</summary>
        public int MaxResponseBodyBytes
        {
            get { return _MaxResponseBodyBytes; }
            set { _MaxResponseBodyBytes = Math.Clamp(value, 0, 1024 * 1024); }
        }

        /// <summary>Retention in days before a captured row is eligible for pruning.</summary>
        public int RetentionDays
        {
            get { return _RetentionDays; }
            set { _RetentionDays = Math.Clamp(value, 1, 3650); }
        }

        /// <summary>Interval in minutes between prune passes.</summary>
        public int PruneIntervalMinutes
        {
            get { return _PruneIntervalMinutes; }
            set { _PruneIntervalMinutes = Math.Clamp(value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private int _MaxRequestBodyBytes = 65536;
        private int _MaxResponseBodyBytes = 65536;
        private int _RetentionDays = 30;
        private int _PruneIntervalMinutes = 60;

        #endregion
    }
}
