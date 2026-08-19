namespace Pneuma.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A unit of ingestion work moving a single artifact through the pipeline stages.
    /// </summary>
    public class IngestionJob
    {
        #region Public-Members

        /// <summary>Job identifier (prefix "job_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Subject identifier.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>Originating link identifier.</summary>
        public string LinkId
        {
            get { return _LinkId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(LinkId)); _LinkId = value; }
        }

        /// <summary>The source URL being ingested.</summary>
        public string SourceUrl { get; set; } = String.Empty;

        /// <summary>Overall job status.</summary>
        public IngestionStatusEnum Status { get; set; } = IngestionStatusEnum.Queued;

        /// <summary>Current (or failed) pipeline stage.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>Number of processing attempts.</summary>
        public int AttemptCount
        {
            get { return _AttemptCount; }
            set { _AttemptCount = value < 0 ? 0 : value; }
        }

        /// <summary>Last error message, if failed.</summary>
        public string? Error { get; set; } = null;

        /// <summary>Detected document type from DocumentAtom.</summary>
        public string? DocumentType { get; set; } = null;

        /// <summary>BLOB storage key of the persisted raw artifact.</summary>
        public string? BlobKey { get; set; } = null;

        /// <summary>Chosen Partio embedding endpoint identifier (e.g. "default"); null to resolve server-side.</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Chosen Partio completion endpoint identifier (e.g. "default"); null to resolve server-side.</summary>
        public string? CompletionEndpointId { get; set; } = null;

        /// <summary>RecallDB collection identifier the ingested chunks are stored in and searched from.</summary>
        public string? CollectionId { get; set; } = null;

        /// <summary>Graph node identifiers created or updated during merge.</summary>
        public List<string> GraphNodeIds { get; set; } = new List<string>();

        /// <summary>UTC time processing started, if started.</summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>UTC time processing completed or failed, if finished.</summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateJobId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private string _LinkId = String.Empty;
        private int _AttemptCount = 0;

        #endregion
    }
}
