namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>A background ontology operation on a subject.</summary>
    public class OntologyOperation
    {
        /// <summary>Operation identifier (prefix "oop_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>Kind.</summary>
        public OntologyOperationKindEnum Kind { get; set; } = OntologyOperationKindEnum.Validate;

        /// <summary>State.</summary>
        public OntologyOperationStatusEnum Status { get; set; } = OntologyOperationStatusEnum.Queued;

        /// <summary>Ontology version in effect.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>Requesting user.</summary>
        public string? RequestedByUserId { get; set; } = null;

        /// <summary>Cells to sample (DriftCheck).</summary>
        public int SampleSize { get; set; } = 10;

        /// <summary>Items to process.</summary>
        public int Total { get; set; } = 0;

        /// <summary>Items processed.</summary>
        public int Processed { get; set; } = 0;

        /// <summary>Items with a finding.</summary>
        public int Changed { get; set; } = 0;

        /// <summary>Taxonomy links added.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Taxonomy links removed.</summary>
        public int Removed { get; set; } = 0;

        /// <summary>Drift rate, 0 to 1.</summary>
        public double DriftRate { get; set; } = 0;

        /// <summary>Error, if failed.</summary>
        public string? Error { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC start timestamp.</summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>UTC finish timestamp.</summary>
        public DateTime? FinishedUtc { get; set; } = null;
    }
}
