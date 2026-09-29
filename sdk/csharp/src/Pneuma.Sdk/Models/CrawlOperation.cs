namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>One run of a crawl plan.</summary>
    public class CrawlOperation
    {
        /// <summary>Identifier (prefix "cop_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The plan.</summary>
        public string PlanId { get; set; } = string.Empty;

        /// <summary>The plan's subject.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>What started the run.</summary>
        public CrawlTriggerEnum Trigger { get; set; } = CrawlTriggerEnum.Manual;

        /// <summary>The run's state.</summary>
        public CrawlOperationStatusEnum Status { get; set; } = CrawlOperationStatusEnum.Running;

        /// <summary>Objects listed.</summary>
        public int Enumerated { get; set; } = 0;

        /// <summary>New objects ingested.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Changed objects re-ingested.</summary>
        public int Updated { get; set; } = 0;

        /// <summary>Failed objects re-ingested.</summary>
        public int Retried { get; set; } = 0;

        /// <summary>Unchanged objects.</summary>
        public int Unchanged { get; set; } = 0;

        /// <summary>Links deleted.</summary>
        public int Deleted { get; set; } = 0;

        /// <summary>Objects gone but kept.</summary>
        public int Missing { get; set; } = 0;

        /// <summary>Objects filtered out.</summary>
        public int Skipped { get; set; } = 0;

        /// <summary>Objects that failed.</summary>
        public int Failed { get; set; } = 0;

        /// <summary>Total size listed.</summary>
        public long BytesEnumerated { get; set; } = 0;

        /// <summary>Deletions waiting for confirmation.</summary>
        public int HeldDeletions { get; set; } = 0;

        /// <summary>Why the run failed or was held.</summary>
        public string? Error { get; set; } = null;

        /// <summary>When the run started.</summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When enumeration finished.</summary>
        public DateTime? EnumeratedUtc { get; set; } = null;

        /// <summary>When changes were dispatched.</summary>
        public DateTime? DispatchedUtc { get; set; } = null;

        /// <summary>When the run finished.</summary>
        public DateTime? FinishedUtc { get; set; } = null;
    }
}
