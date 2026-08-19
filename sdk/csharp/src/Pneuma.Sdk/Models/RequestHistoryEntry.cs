namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A captured HTTP request and response. List responses omit bodies to keep payloads small.
    /// </summary>
    public class RequestHistoryEntry
    {
        /// <summary>Entry identifier (prefix "req_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Tenant identifier. Null for unauthenticated requests.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>User identifier. Null for unauthenticated requests.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Principal display name, if resolved.</summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>HTTP method.</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>Route template that matched, or raw path.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>Full request URL including query string.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Response HTTP status code.</summary>
        public int StatusCode { get; set; } = 0;

        /// <summary>Request duration in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>Client source IP.</summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>Request headers (secrets redacted). Present only on the full-entry read.</summary>
        public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>Request body (may be truncated). Present only on the full-entry read.</summary>
        public string? RequestBody { get; set; } = null;

        /// <summary>Original request body length in bytes, before truncation.</summary>
        public long RequestBodyBytes { get; set; } = 0;

        /// <summary>Whether the request body was truncated.</summary>
        public bool RequestBodyTruncated { get; set; } = false;

        /// <summary>Response headers. Present only on the full-entry read.</summary>
        public Dictionary<string, string> ResponseHeaders { get; set; } = new Dictionary<string, string>();

        /// <summary>Response body (may be truncated). Present only on the full-entry read.</summary>
        public string? ResponseBody { get; set; } = null;

        /// <summary>Original response body length in bytes, before truncation.</summary>
        public long ResponseBodyBytes { get; set; } = 0;

        /// <summary>Whether the response body was truncated.</summary>
        public bool ResponseBodyTruncated { get; set; } = false;

        /// <summary>UTC time the request began.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC time the response was sent.</summary>
        public DateTime? CompletedUtc { get; set; } = null;
    }
}
