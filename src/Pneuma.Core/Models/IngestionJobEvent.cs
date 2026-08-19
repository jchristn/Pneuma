namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A per-stage audit entry for an ingestion job.
    /// </summary>
    public class IngestionJobEvent
    {
        #region Public-Members

        /// <summary>Event identifier (prefix "jev_").</summary>
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

        /// <summary>Job identifier.</summary>
        public string JobId
        {
            get { return _JobId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(JobId)); _JobId = value; }
        }

        /// <summary>Pipeline stage this event describes.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>Status of the stage.</summary>
        public IngestionStatusEnum Status { get; set; } = IngestionStatusEnum.Processing;

        /// <summary>Human-readable message.</summary>
        public string? Message { get; set; } = null;

        /// <summary>Stage duration in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateJobEventId();
        private string _TenantId = String.Empty;
        private string _JobId = String.Empty;

        #endregion
    }
}
