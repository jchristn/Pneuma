namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// One object a crawl plan has seen (a page, a file, a bucket object), with the version it was last ingested at and
    /// the link that holds its content. The set of a plan's crawl objects is the baseline each run is compared with, so
    /// the delta survives restarts and works with more than one server.
    /// </summary>
    public class CrawlObject
    {
        #region Public-Members

        /// <summary>Crawl object identifier (prefix "cob_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>The crawl plan that saw the object.</summary>
        public string PlanId { get; set; } = String.Empty;

        /// <summary>The object's key in its source (a normalized URL, an object key, or a path).</summary>
        public string ExternalKey { get; set; } = String.Empty;

        /// <summary>The link that holds the object's content, or null when it was never ingested.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The version the object was last ingested at (an ETag, a last-modified date, or a size and date), or null.</summary>
        public string? VersionToken { get; set; } = null;

        /// <summary>Size in bytes when known; 0 otherwise. Minimum 0.</summary>
        public long SizeBytes
        {
            get { return _SizeBytes; }
            set { _SizeBytes = Math.Max(0L, value); }
        }

        /// <summary>Content type when known.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>The object's state.</summary>
        public CrawlObjectStatusEnum Status { get; set; } = CrawlObjectStatusEnum.Active;

        /// <summary>The last ingestion error, when the status is Failed.</summary>
        public string? LastError { get; set; } = null;

        /// <summary>The operation that last saw the object.</summary>
        public string? LastOperationId { get; set; } = null;

        /// <summary>When the object was first seen.</summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>When the object was last seen in the source.</summary>
        public DateTime LastSeenUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateCrawlObjectId();
        private long _SizeBytes = 0;

        #endregion
    }
}
