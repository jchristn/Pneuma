namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// A durable security event: denials, bypasses, session lifecycle, and RBAC changes.
    /// </summary>
    public class AuditRecord
    {
        /// <summary>Audit record identifier (prefix "aud_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Event type.</summary>
        public AuditEventTypeEnum EventType { get; set; } = AuditEventTypeEnum.AuthorizationDenied;

        /// <summary>Tenant identifier, if resolvable.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>User identifier, if resolvable.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Credential identifier, if resolvable.</summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>Session identifier, if resolvable.</summary>
        public string? SessionId { get; set; } = null;

        /// <summary>Target resource identifier, if resolvable.</summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>Principal type, if resolvable.</summary>
        public PrincipalTypeEnum? PrincipalType { get; set; } = null;

        /// <summary>Authentication scheme, if resolvable.</summary>
        public AuthSchemeEnum? AuthScheme { get; set; } = null;

        /// <summary>Request tracking / correlation identifier.</summary>
        public string? RequestId { get; set; } = null;

        /// <summary>HTTP method.</summary>
        public string? HttpMethod { get; set; } = null;

        /// <summary>URL path.</summary>
        public string? UrlPath { get; set; } = null;

        /// <summary>Source IP address.</summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>Authentication result, if applicable.</summary>
        public AuthenticationResultEnum? AuthenticationResult { get; set; } = null;

        /// <summary>Authorization result, if applicable.</summary>
        public AuthorizationResultEnum? AuthorizationResult { get; set; } = null;

        /// <summary>Required resource type for the denied/evaluated operation.</summary>
        public ResourceTypeEnum? RequiredResourceType { get; set; } = null;

        /// <summary>Required operation for the denied/evaluated operation.</summary>
        public OperationTypeEnum? RequiredOperation { get; set; } = null;

        /// <summary>Free-text denial reason.</summary>
        public string? DenialReason { get; set; } = null;

        /// <summary>Free-text bypass reason when authorization was skipped.</summary>
        public string? BypassReason { get; set; } = null;

        /// <summary>Response status code.</summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
