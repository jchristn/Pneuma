namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Session token issuance/validation response.
    /// </summary>
    public class TokenResponse
    {
        /// <summary>Opaque bearer token.</summary>
        public string Token { get; set; } = String.Empty;

        /// <summary>UTC expiration.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>Principal type.</summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>Tenant identifier.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>User identifier.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Principal display name.</summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>Email address.</summary>
        public string? Email { get; set; } = null;

        /// <summary>Whether the principal is a global administrator.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>Whether the principal is a tenant administrator.</summary>
        public bool IsTenantAdmin { get; set; } = false;
    }
}
