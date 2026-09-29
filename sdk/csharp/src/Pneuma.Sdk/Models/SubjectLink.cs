namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A content link submitted by an subject for ingestion.
    /// </summary>
    public class SubjectLink
    {
        /// <summary>Link identifier (prefix "lnk_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier this link belongs to.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>The submitted URL.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Operator-supplied labels attached to every chunk this link produced and to its source graph node.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Operator-supplied key/value tags attached to every chunk this link produced and to its source graph node.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>Identifier of the user who submitted the link.</summary>
        public string? SubmittedByUserId { get; set; } = null;

        /// <summary>Current processing status.</summary>
        public SubjectLinkStatusEnum Status { get; set; } = SubjectLinkStatusEnum.Submitted;

        /// <summary>UTC timestamp of the last successful ingestion, if any.</summary>
        public DateTime? LastIngestedUtc { get; set; } = null;

        /// <summary>Last error message, if the most recent ingestion failed.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>Where the content comes from: Url, Inline (pushed content), or Crawl.</summary>
        public string SourceKind { get; set; } = "Url";

        /// <summary>Caller-chosen key identifying pushed or crawled content within the subject, or null.</summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>Content type the content was pushed or found with, or null.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Size of pushed or crawled content in bytes; 0 when unknown.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>The crawl plan that owns this link, or null.</summary>
        public string? CrawlPlanId { get; set; } = null;

        /// <summary>Minutes between scheduled refresh checks: null follows the subject's default, 0 is off.</summary>
        public int? RefreshIntervalMinutes { get; set; } = null;

        /// <summary>When the link is next checked for changes, or null when its refresh is off.</summary>
        public DateTime? NextRefreshUtc { get; set; } = null;

        /// <summary>When the link was last checked for changes.</summary>
        public DateTime? LastRefreshUtc { get; set; } = null;

        /// <summary>Failed checks in a row; checks back off while this is above zero.</summary>
        public int RefreshFailures { get; set; } = 0;

        /// <summary>The source's ETag at the last check, sent as If-None-Match.</summary>
        public string? SourceETag { get; set; } = null;

        /// <summary>The source's Last-Modified at the last check, sent as If-Modified-Since.</summary>
        public DateTime? SourceLastModifiedUtc { get; set; } = null;

        /// <summary>The job whose output is the link's live version, or null until the link is first ingested.</summary>
        public string? CurrentJobId { get; set; } = null;

        /// <summary>Why the most recent ingestion failed, or null when it did not fail.</summary>
        public IngestionFailureCategoryEnum? FailureCategory { get; set; } = null;

        /// <summary>Number of warnings the most recent successful ingestion recorded.</summary>
        public int WarningCount { get; set; } = 0;

        /// <summary>Whether the link is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the link is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
