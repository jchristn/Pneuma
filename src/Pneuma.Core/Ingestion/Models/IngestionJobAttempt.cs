namespace Pneuma.Core.Ingestion.Models
{
    using System;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// One attempt at running an ingestion job. A job retried after a transient failure has several attempts; each
    /// records where it ended and why, so an operator can see the whole history rather than only the last error.
    /// </summary>
    public class IngestionJobAttempt
    {
        #region Public-Members

        /// <summary>Attempt identifier (prefix "jatt_").</summary>
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

        /// <summary>The job this attempt belongs to.</summary>
        public string JobId
        {
            get { return _JobId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(JobId)); _JobId = value; }
        }

        /// <summary>The 1-based attempt number. Minimum 1.</summary>
        public int AttemptNumber
        {
            get { return _AttemptNumber; }
            set { _AttemptNumber = Math.Max(1, value); }
        }

        /// <summary>True when the attempt completed the job.</summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>The stage the attempt ended in.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>The failure category, or null for a successful attempt.</summary>
        public IngestionFailureCategoryEnum? FailureCategory { get; set; } = null;

        /// <summary>The failure message, or null for a successful attempt.</summary>
        public string? Message { get; set; } = null;

        /// <summary>UTC time the attempt started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time the attempt ended.</summary>
        public DateTime EndedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateJobAttemptId();
        private string _TenantId = String.Empty;
        private string _JobId = String.Empty;
        private int _AttemptNumber = 1;

        #endregion
    }
}
