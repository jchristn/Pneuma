namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One time bucket of chat analytics: the turn count, average generation time, and the average latency of
    /// each answer-pipeline stage for turns/events whose creation falls in the bucket. The per-stage
    /// latencies drive the stacked latency-over-time chart.
    /// </summary>
    public class AnalyticsBucket
    {
        #region Public-Members

        /// <summary>UTC start of the bucket.</summary>
        public DateTime BucketUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Number of turns in the bucket.</summary>
        public int Count { get; set; } = 0;

        /// <summary>Average generation time in milliseconds for the bucket.</summary>
        public double AvgGenerationMs { get; set; } = 0;

        /// <summary>Average latency in milliseconds per answer-pipeline stage in the bucket, keyed by stage name.</summary>
        public Dictionary<string, double> StageLatencies { get; set; } = new Dictionary<string, double>();

        #endregion
    }
}
