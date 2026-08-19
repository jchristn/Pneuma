namespace Pneuma.Core.Security
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// The internal, platform-controlled contents of an opaque session token.
    /// </summary>
    public class TokenPayload
    {
        #region Public-Members

        /// <summary>Session identifier.</summary>
        public string SessionId { get; set; } = String.Empty;

        /// <summary>Principal type.</summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>Tenant identifier.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Account identifier.</summary>
        public string? AccountId { get; set; } = null;

        /// <summary>User identifier.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Credential identifier.</summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>Administrator identifier.</summary>
        public string? AdministratorId { get; set; } = null;

        /// <summary>Random token identifier / nonce.</summary>
        public string TokenId { get; set; } = String.Empty;

        /// <summary>Issuing authentication scheme.</summary>
        public AuthSchemeEnum Scheme { get; set; } = AuthSchemeEnum.BearerToken;

        /// <summary>UTC issuance timestamp.</summary>
        public DateTime IssuedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC expiration timestamp.</summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddHours(1);

        #endregion
    }
}
