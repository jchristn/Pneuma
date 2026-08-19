namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// One time bucket of request activity for chart rendering.
    /// </summary>
    public class RequestHistoryBucket
    {
        #region Public-Members

        /// <summary>Inclusive UTC start of the bucket.</summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>Exclusive UTC end of the bucket.</summary>
        public DateTime BucketEndUtc { get; set; }

        /// <summary>Count of successful (2xx/3xx) responses.</summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>Count of failed (4xx/5xx) responses.</summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>Average duration in milliseconds within the bucket.</summary>
        public double AverageDurationMs { get; set; } = 0;

        #endregion
    }
}
