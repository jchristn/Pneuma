namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>What one crawl operation did with one object.</summary>
    public class CrawlOperationObject
    {
        /// <summary>Identifier (prefix "coo_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The operation.</summary>
        public string OperationId { get; set; } = string.Empty;

        /// <summary>The object's key.</summary>
        public string ExternalKey { get; set; } = string.Empty;

        /// <summary>What the operation did.</summary>
        public CrawlActionEnum Action { get; set; } = CrawlActionEnum.Add;

        /// <summary>Null while pending, then the outcome.</summary>
        public bool? Succeeded { get; set; } = null;

        /// <summary>The link touched.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The ingestion job queued.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Why it was skipped or failed.</summary>
        public string? Detail { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
