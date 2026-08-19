namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>
    /// An ingestion job with its per-stage event history.
    /// </summary>
    public class IngestionJobDetail
    {
        /// <summary>The job.</summary>
        public IngestionJob Job { get; set; } = new IngestionJob();

        /// <summary>The chronological stage events.</summary>
        public List<IngestionJobEvent> Events { get; set; } = new List<IngestionJobEvent>();
    }
}
