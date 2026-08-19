namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Typed filter for querying and summarizing request history.
    /// </summary>
    public class RequestHistoryFilter
    {
        #region Public-Members

        /// <summary>Tenant identifier scope. Null means all tenants (admin only).</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>User identifier scope.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>HTTP method exact match.</summary>
        public string? Method { get; set; } = null;

        /// <summary>Response status code exact match.</summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>Substring match against the path.</summary>
        public string? PathContains { get; set; } = null;

        /// <summary>Inclusive lower bound on creation time (UTC).</summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>Inclusive upper bound on creation time (UTC).</summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>One-based page number.</summary>
        public int PageNumber
        {
            get { return _PageNumber; }
            set { _PageNumber = value < 1 ? 1 : value; }
        }

        /// <summary>Page size.</summary>
        public int PageSize
        {
            get { return _PageSize; }
            set { _PageSize = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>Bucket size in minutes for the summary call.</summary>
        public int BucketMinutes
        {
            get { return _BucketMinutes; }
            set { _BucketMinutes = Math.Clamp(value, 1, 1440); }
        }

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;
        private int _BucketMinutes = 15;

        #endregion
    }
}
