namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>
    /// The index row of a cached classification result. The result itself (a candidate subgraph) is stored in the blob
    /// store under <see cref="BlobKey"/>; the row lets the cache be counted, pruned, and removed with its tenant or subject.
    /// </summary>
    public class ClassificationCacheEntry
    {
        #region Public-Members

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>SHA-256 (hex) of the full classification request.</summary>
        public string CacheKey { get; set; } = String.Empty;

        /// <summary>Subject whose ingestion first stored the entry.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Blob store key of the cached candidate subgraph.</summary>
        public string BlobKey { get; set; } = String.Empty;

        /// <summary>Times the entry was reused.</summary>
        public int Hits { get; set; } = 0;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC timestamp of the last reuse, or of creation.</summary>
        public DateTime LastUsedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
