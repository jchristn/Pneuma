namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// A per-subject chat analytics report over a time window: headline overview, a time series of volume and
    /// latency, and per-stage latency. Backs the dashboard Analytics view.
    /// </summary>
    public class AnalyticsReport
    {
        #region Public-Members

        /// <summary>Headline metrics for the window.</summary>
        public AnalyticsOverview Overview { get; set; } = new AnalyticsOverview();

        /// <summary>Volume and latency over time, oldest bucket first.</summary>
        public List<AnalyticsBucket> Timeseries { get; set; } = new List<AnalyticsBucket>();

        /// <summary>Per-stage latency, slowest average first.</summary>
        public List<AnalyticsStageStat> Stages { get; set; } = new List<AnalyticsStageStat>();

        #endregion
    }
}
