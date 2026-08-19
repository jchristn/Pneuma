namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A per-stage audit entry for an ingestion job.
    /// </summary>
    public class IngestionJobEvent
    {
        /// <summary>Event identifier (prefix "jev_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Job identifier.</summary>
        public string JobId { get; set; } = string.Empty;

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
    }
}
