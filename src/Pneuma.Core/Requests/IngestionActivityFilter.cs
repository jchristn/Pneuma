namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Typed filter for summarizing ingestion activity (per-stage event counts over time).
    /// </summary>
    public class IngestionActivityFilter
    {
        #region Public-Members

        /// <summary>Tenant identifier scope. Null means all tenants (admin only).</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Subject identifier scope. Null means all subjects.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Inclusive lower bound on event creation time (UTC).</summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>Inclusive upper bound on event creation time (UTC).</summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>Bucket size in minutes for the summary call.</summary>
        public int BucketMinutes
        {
            get { return _BucketMinutes; }
            set { _BucketMinutes = Math.Clamp(value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private int _BucketMinutes = 15;

        #endregion
    }
}
