namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// One time bucket of ingestion activity, broken down by pipeline stage, for chart rendering.
    /// </summary>
    public class IngestionActivityBucket
    {
        #region Public-Members

        /// <summary>Inclusive UTC start of the bucket.</summary>
        public DateTime BucketStartUtc { get; set; }

        /// <summary>Exclusive UTC end of the bucket.</summary>
        public DateTime BucketEndUtc { get; set; }

        /// <summary>Total stage events within the bucket across all stages.</summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>Per-stage event counts within the bucket (stages with zero activity are omitted).</summary>
        public List<IngestionStageCount> Stages { get; set; } = new List<IngestionStageCount>();

        #endregion
    }
}
