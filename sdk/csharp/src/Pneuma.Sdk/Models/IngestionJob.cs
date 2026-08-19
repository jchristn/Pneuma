namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A unit of ingestion work moving a single artifact through the pipeline stages.
    /// </summary>
    public class IngestionJob
    {
        /// <summary>Job identifier (prefix "job_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>Originating link identifier.</summary>
        public string LinkId { get; set; } = string.Empty;

        /// <summary>The source URL being ingested.</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>Overall job status.</summary>
        public IngestionStatusEnum Status { get; set; } = IngestionStatusEnum.Queued;

        /// <summary>Current (or failed) pipeline stage.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>Number of processing attempts.</summary>
        public int AttemptCount { get; set; } = 0;

        /// <summary>Last error message, if failed.</summary>
        public string? Error { get; set; } = null;

        /// <summary>Detected document type.</summary>
        public string? DocumentType { get; set; } = null;

        /// <summary>BLOB storage key of the persisted raw artifact.</summary>
        public string? BlobKey { get; set; } = null;

        /// <summary>Graph node identifiers created or updated during merge.</summary>
        public List<string> GraphNodeIds { get; set; } = new List<string>();

        /// <summary>Search document identifiers created during indexing.</summary>
        public List<string> VerbexDocumentIds { get; set; } = new List<string>();

        /// <summary>UTC time processing started, if started.</summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>UTC time processing completed or failed, if finished.</summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
