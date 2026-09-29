namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>One object a crawl plan tracks.</summary>
    public class CrawlObject
    {
        /// <summary>Identifier (prefix "cob_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The plan.</summary>
        public string PlanId { get; set; } = string.Empty;

        /// <summary>The object's key in its source.</summary>
        public string ExternalKey { get; set; } = string.Empty;

        /// <summary>The link holding its content.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The version last ingested.</summary>
        public string? VersionToken { get; set; } = null;

        /// <summary>Size in bytes.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>Content type.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Active, Missing, Failed, or Excluded.</summary>
        public CrawlObjectStatusEnum Status { get; set; } = CrawlObjectStatusEnum.Active;

        /// <summary>The last ingestion error.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>The operation that last saw it.</summary>
        public string? LastOperationId { get; set; } = null;

        /// <summary>When first seen.</summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When last seen.</summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;
    }
}
