namespace Pneuma.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A content link submitted by an subject for ingestion.
    /// </summary>
    public class SubjectLink
    {
        #region Public-Members

        /// <summary>Link identifier (prefix "lnk_").</summary>
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

        /// <summary>Subject identifier this link belongs to.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>The submitted URL.</summary>
        public string Url
        {
            get { return _Url; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Url)); _Url = value; }
        }

        /// <summary>Optional operator-facing title.</summary>
        public string? Title { get; set; } = null;

        /// <summary>
        /// Operator-supplied labels (plain strings) attached to every chunk this link produces and to the
        /// link's source graph node, so retrieval can be scoped to them. Empty when none were supplied.
        /// </summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>
        /// Operator-supplied key/value tags attached to every chunk this link produces and to the link's
        /// source graph node, so retrieval can be scoped to them. Empty when none were supplied.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>Identifier of the user who submitted the link.</summary>
        public string? SubmittedByUserId { get; set; } = null;

        /// <summary>Current processing status.</summary>
        public SubjectLinkStatusEnum Status { get; set; } = SubjectLinkStatusEnum.Submitted;

        /// <summary>UTC timestamp of the last successful ingestion, if any.</summary>
        public DateTime? LastIngestedUtc { get; set; } = null;

        /// <summary>
        /// Content hash (hex SHA-256) of the source bytes as of the last successful ingestion. Used for delta
        /// detection: a re-ingestion whose freshly-fetched content hashes to this same value skips the expensive
        /// extract/classify/embed/index work and completes immediately. Null until the first successful ingest.
        /// </summary>
        public string? ContentHash { get; set; } = null;

        /// <summary>Last error message, if the most recent ingestion failed.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>
        /// The job whose output is the link's live version (the last job that indexed it). Earlier jobs' chunks and
        /// Source and Cell nodes are removed once a newer job finishes, so only this version is searchable. Null until
        /// the link is first ingested.
        /// </summary>
        public string? CurrentJobId { get; set; } = null;

        /// <summary>Where the link's content comes from: a URL, pushed content, or a crawl plan. Default Url.</summary>
        public SourceKindEnum SourceKind { get; set; } = SourceKindEnum.Url;

        /// <summary>
        /// A caller-chosen key that identifies the content within the subject (pushed content upserts by it; a crawled
        /// link's key is its object key or URL). Unique per subject when set.
        /// </summary>
        public string? ExternalKey { get; set; } = null;

        /// <summary>The content type the content was pushed or found with, when known.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Size of the pushed or crawled content in bytes; 0 when unknown. Minimum 0.</summary>
        public long SizeBytes
        {
            get { return _SizeBytes; }
            set { _SizeBytes = Math.Max(0L, value); }
        }

        /// <summary>
        /// Minutes between scheduled refreshes of this link: null uses the subject's default, 0 turns refresh off,
        /// otherwise 60 to 525600. Only URL links that no crawl plan owns are refreshed.
        /// </summary>
        public int? RefreshIntervalMinutes { get; set; } = null;

        /// <summary>When the link is next checked for changes, or null when it is not refreshed.</summary>
        public DateTime? NextRefreshUtc { get; set; } = null;

        /// <summary>When the link was last checked for changes, or null.</summary>
        public DateTime? LastRefreshUtc { get; set; } = null;

        /// <summary>Refresh checks that failed in a row (reset by a successful check). Minimum 0.</summary>
        public int RefreshFailures
        {
            get { return _RefreshFailures; }
            set { _RefreshFailures = Math.Max(0, value); }
        }

        /// <summary>The ETag the source returned at the last check, sent back as If-None-Match.</summary>
        public string? SourceETag { get; set; } = null;

        /// <summary>The Last-Modified time the source returned at the last check, sent back as If-Modified-Since.</summary>
        public DateTime? SourceLastModifiedUtc { get; set; } = null;

        /// <summary>The crawl plan that created and owns this link, or null.</summary>
        public string? CrawlPlanId { get; set; } = null;

        /// <summary>Why the most recent ingestion failed, or null when it did not fail.</summary>
        public IngestionFailureCategoryEnum? FailureCategory { get; set; } = null;

        /// <summary>
        /// Number of warnings the most recent successful ingestion recorded (work it dropped but completed without).
        /// Zero for a complete ingest. Minimum 0.
        /// </summary>
        public int WarningCount
        {
            get { return _WarningCount; }
            set { _WarningCount = Math.Max(0, value); }
        }

        /// <summary>Whether the link is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the link is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>
        /// Lifecycle state of a tracked background cascade deletion. A live link is
        /// <see cref="LinkDeletionStatusEnum.None"/>; deleting a link marks it Pending and the background
        /// worker runs the cascade, so the request that starts the delete is never blocked by it.
        /// </summary>
        public LinkDeletionStatusEnum DeletionStatus { get; set; } = LinkDeletionStatusEnum.None;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _RefreshFailures = 0;

        private string _Id = IdGenerator.GenerateLinkId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private string _Url = String.Empty;
        private int _WarningCount = 0;
        private long _SizeBytes = 0;

        #endregion
    }
}
