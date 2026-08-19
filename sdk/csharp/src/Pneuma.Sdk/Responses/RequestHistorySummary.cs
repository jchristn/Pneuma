namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Time-bucketed request activity summary, including empty buckets across the range.
    /// </summary>
    public class RequestHistorySummary
    {
        /// <summary>Total number of requests across the range.</summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>Total successful (2xx/3xx) responses.</summary>
        public long TotalSuccess { get; set; } = 0;

        /// <summary>Total failed (4xx/5xx) responses.</summary>
        public long TotalFailure { get; set; } = 0;

        /// <summary>Average duration in milliseconds across the range.</summary>
        public double AverageDurationMs { get; set; } = 0;

        /// <summary>Ordered buckets covering the requested range.</summary>
        public List<RequestHistoryBucket> Buckets { get; set; } = new List<RequestHistoryBucket>();
    }
}
