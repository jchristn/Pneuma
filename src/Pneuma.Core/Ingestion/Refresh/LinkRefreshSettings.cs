namespace Pneuma.Core.Ingestion.Refresh
{
    using System;

    /// <summary>Server settings for scheduled link refresh (the <c>LinkRefresh</c> section of pneuma.json).</summary>
    public class LinkRefreshSettings
    {
        #region Public-Members

        /// <summary>Check due links on this server. Default true; "refresh now" works either way.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Seconds between passes. Default 60; clamped to [10, 3600].</summary>
        public int IntervalSeconds
        {
            get { return _IntervalSeconds; }
            set { _IntervalSeconds = Math.Clamp(value, 10, 3600); }
        }

        /// <summary>Most links checked per pass. Default 50; clamped to [1, 1000].</summary>
        public int BatchSize
        {
            get { return _BatchSize; }
            set { _BatchSize = Math.Clamp(value, 1, 1000); }
        }

        #endregion

        #region Private-Members

        private int _IntervalSeconds = 60;
        private int _BatchSize = 50;

        #endregion
    }
}
