namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Time-bucketed summary of ingestion activity, broken down by pipeline stage. Mirrors the shape of
    /// <see cref="RequestHistorySummary"/> but stacks by stage instead of success/failure.
    /// </summary>
    public class IngestionActivitySummary
    {
        #region Public-Members

        /// <summary>Total stage events across all buckets.</summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>Overall per-stage event totals across the whole range, ordered by pipeline stage.</summary>
        public List<IngestionStageCount> Totals { get; set; } = new List<IngestionStageCount>();

        /// <summary>Ordered time buckets spanning the requested range.</summary>
        public List<IngestionActivityBucket> Buckets { get; set; } = new List<IngestionActivityBucket>();

        #endregion
    }
}
