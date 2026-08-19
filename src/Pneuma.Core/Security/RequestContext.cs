namespace Pneuma.Core.Security
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Authenticated request context, created at the start of request processing and stashed in the
    /// Watson HTTP context metadata for handlers to authorize against.
    /// </summary>
    public class RequestContext
    {
        #region Public-Members

        /// <summary>Unique request identifier for tracing.</summary>
        public string RequestId { get; set; } = Guid.NewGuid().ToString();

        /// <summary>Whether the request is authenticated.</summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>Tenant identifier.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>User identifier.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Whether the principal is a global (system) administrator.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>Whether the principal is a tenant administrator.</summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>Principal display name, if resolved.</summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>Principal email, if resolved.</summary>
        public string? Email { get; set; } = null;

        /// <summary>Optional correlation identifier supplied by the caller.</summary>
        public string? CorrelationId { get; set; } = null;

        /// <summary>Optional scope collection.</summary>
        public List<string> Scopes { get; set; } = new List<string>();

        /// <summary>HTTP method of the request.</summary>
        public string? HttpMethod { get; set; } = null;

        /// <summary>Raw URL of the request.</summary>
        public string? Url { get; set; } = null;

        /// <summary>Source IP address.</summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>Embedded authentication context.</summary>
        public AuthenticationContext Authentication { get; set; } = new AuthenticationContext();

        /// <summary>Embedded authorization context.</summary>
        public AuthorizationContext Authorization { get; set; } = new AuthorizationContext();

        #endregion
    }
}
