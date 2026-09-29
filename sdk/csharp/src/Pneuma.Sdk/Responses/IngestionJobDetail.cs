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

        /// <summary>The job's attempts, oldest first.</summary>
        public List<IngestionJobAttempt> Attempts { get; set; } = new List<IngestionJobAttempt>();

        /// <summary>Remediation for the job's failure category, or null when the job has not failed.</summary>
        public string? Remediation { get; set; } = null;
    }
}
