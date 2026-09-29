namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>One attempt at running an ingestion job: where it ended and why.</summary>
    public class IngestionJobAttempt
    {
        /// <summary>Attempt identifier (prefix "jatt_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The job this attempt belongs to.</summary>
        public string JobId { get; set; } = string.Empty;

        /// <summary>The 1-based attempt number.</summary>
        public int AttemptNumber { get; set; } = 1;

        /// <summary>True when the attempt completed the job.</summary>
        public bool Succeeded { get; set; } = false;

        /// <summary>The stage the attempt ended in.</summary>
        public IngestionStageEnum Stage { get; set; } = IngestionStageEnum.Pending;

        /// <summary>The failure category, or null for a successful attempt.</summary>
        public IngestionFailureCategoryEnum? FailureCategory { get; set; } = null;

        /// <summary>The failure message, or null.</summary>
        public string? Message { get; set; } = null;

        /// <summary>UTC time the attempt started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time the attempt ended.</summary>
        public DateTime EndedUtc { get; set; } = DateTime.UtcNow;
    }
}
