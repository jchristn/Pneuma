namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A non-interactive API credential owned by a user and bound to a tenant.
    /// </summary>
    public class Credential
    {
        #region Public-Members

        /// <summary>Credential identifier (prefix "crd_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Owning user identifier.</summary>
        public string UserId
        {
            get { return _UserId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(UserId)); _UserId = value; }
        }

        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Public access key (format "access_" + random).</summary>
        public string AccessKey
        {
            get { return _AccessKey; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(AccessKey)); _AccessKey = value; }
        }

        /// <summary>Encrypted secret key or verifier material. Raw secret is shown only once at creation.</summary>
        public string SecretKeyEncrypted { get; set; } = String.Empty;

        /// <summary>Last four characters of the raw secret, for operator reference.</summary>
        public string SecretKeyLast4 { get; set; } = String.Empty;

        /// <summary>Authentication mode.</summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.DirectHeader;

        /// <summary>UTC timestamp of last use, if any.</summary>
        public DateTime? LastUsedUtc { get; set; } = null;

        /// <summary>Optional UTC expiration timestamp.</summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>Whether the credential is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the credential is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateCredentialId();
        private string _TenantId = String.Empty;
        private string _UserId = String.Empty;
        private string _AccessKey = String.Empty;

        #endregion
    }
}
