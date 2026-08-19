namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A revocable, tenant-bound authentication session. Each session resolves to exactly one principal.
    /// </summary>
    public class AuthSession
    {
        #region Public-Members

        /// <summary>Session identifier (prefix "ses_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Tenant identifier.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Account identifier, if applicable.</summary>
        public string? AccountId { get; set; } = null;

        /// <summary>Administrator identifier, if this is an administrator session.</summary>
        public string? AdministratorId { get; set; } = null;

        /// <summary>User identifier, if this is a user session.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Credential identifier, if this is a credential session.</summary>
        public string? CredentialId { get; set; } = null;

        /// <summary>Principal type bound to the session.</summary>
        public PrincipalTypeEnum PrincipalType { get; set; } = PrincipalTypeEnum.User;

        /// <summary>Authentication scheme that created the session.</summary>
        public AuthSchemeEnum AuthScheme { get; set; } = AuthSchemeEnum.PasswordHeaders;

        /// <summary>Random token identifier / nonce.</summary>
        public string TokenId { get; set; } = String.Empty;

        /// <summary>Source IP captured at issuance, if available.</summary>
        public string? SourceIp { get; set; } = null;

        /// <summary>User agent captured at issuance, if available.</summary>
        public string? UserAgent { get; set; } = null;

        /// <summary>UTC expiration timestamp.</summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddHours(1);

        /// <summary>UTC timestamp of last use, if any.</summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>UTC timestamp of revocation, if revoked.</summary>
        public DateTime? RevokedUtc { get; set; } = null;

        /// <summary>Optional revocation reason.</summary>
        public string? RevocationReason { get; set; } = null;

        /// <summary>Whether the session is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the session is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSessionId();

        #endregion
    }
}
