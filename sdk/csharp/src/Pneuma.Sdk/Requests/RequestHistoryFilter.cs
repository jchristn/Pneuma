namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>
    /// Typed filter for querying and summarizing request history. Sent as query-string parameters.
    /// </summary>
    public class RequestHistoryFilter
    {
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
        public int PageNumber { get; set; } = 1;

        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Bucket size in minutes for the summary call.</summary>
        public int BucketMinutes { get; set; } = 15;
    }
}
