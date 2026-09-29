namespace Pneuma.Core.Crawling
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// What one crawl operation did with one object: the action, and (once its ingestion job finishes) the outcome.
    /// Unchanged objects are counted on the operation but not recorded here.
    /// </summary>
    public class CrawlOperationObject
    {
        #region Public-Members

        /// <summary>Identifier (prefix "coo_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>The operation.</summary>
        public string OperationId { get; set; } = String.Empty;

        /// <summary>The object's key.</summary>
        public string ExternalKey { get; set; } = String.Empty;

        /// <summary>What the operation did.</summary>
        public CrawlActionEnum Action { get; set; } = CrawlActionEnum.Add;

        /// <summary>
        /// The outcome: null while the ingestion job is pending, true when it succeeded (or the action needed no job),
        /// false when it failed.
        /// </summary>
        public bool? Succeeded { get; set; } = null;

        /// <summary>The link the action touched, or null.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>The ingestion job queued for the object, or null.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Why the object was skipped or failed, or null.</summary>
        public string? Detail { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateCrawlOperationObjectId();

        #endregion
    }
}
