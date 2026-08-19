namespace Pneuma.Sdk.Responses
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// Credential response. The raw secret key is present only in the create response, shown once.
    /// </summary>
    public class CredentialResponse
    {
        /// <summary>Credential identifier.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Owning user identifier.</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>Name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Access key.</summary>
        public string AccessKey { get; set; } = string.Empty;

        /// <summary>Raw secret key, present only at creation time.</summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>Last four characters of the secret.</summary>
        public string SecretKeyLast4 { get; set; } = string.Empty;

        /// <summary>Authentication mode.</summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.DirectHeader;

        /// <summary>Whether the credential is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Optional expiration.</summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; }
    }
}
