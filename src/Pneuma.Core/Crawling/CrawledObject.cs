namespace Pneuma.Core.Crawling
{
    using System;

    /// <summary>One object a crawler enumerated from its source.</summary>
    public class CrawledObject
    {
        #region Public-Members

        /// <summary>The object's key: a normalized URL, an object key, or a path. Unique within the plan.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>
        /// The URL or URI the link carries (the page URL for web and sitemap plans; an s3://, smb://, or nfs:// URI for
        /// buckets and shares). Defaults to the key.
        /// </summary>
        public string? Uri { get; set; } = null;

        /// <summary>A display title, when the source provides one.</summary>
        public string? Title { get; set; } = null;

        /// <summary>Content type, when known.</summary>
        public string? ContentType { get; set; } = null;

        /// <summary>Size in bytes, or 0 when unknown.</summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// A token that changes when the object changes (an ETag, a last-modified date, or size and date), or null when
        /// the source gives none (every run then re-ingests, and unchanged content completes early by content hash).
        /// </summary>
        public string? VersionToken { get; set; } = null;

        /// <summary>When the object last changed, when known.</summary>
        public DateTime? ModifiedUtc { get; set; } = null;

        /// <summary>True for a folder or prefix entry, which is never ingested.</summary>
        public bool IsFolder { get; set; } = false;

        #endregion
    }
}
