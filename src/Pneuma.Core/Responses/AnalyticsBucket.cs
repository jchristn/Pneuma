namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// One time bucket of chat analytics: the turn count and average generation time for turns whose creation
    /// falls in the bucket.
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

        #endregion
    }
}
