namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;

    /// <summary>
    /// Credential response. The raw secret key is present only in the create response, shown once.
    /// </summary>
    public class CredentialResponse
    {
        #region Public-Members

        /// <summary>Credential identifier.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = String.Empty;

        /// <summary>Owning user identifier.</summary>
        public string UserId { get; set; } = String.Empty;

        /// <summary>Name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Access key.</summary>
        public string AccessKey { get; set; } = String.Empty;

        /// <summary>Raw secret key, present only at creation time.</summary>
        public string? SecretKey { get; set; } = null;

        /// <summary>Last four characters of the secret.</summary>
        public string SecretKeyLast4 { get; set; } = String.Empty;

        /// <summary>Authentication mode.</summary>
        public CredentialAuthModeEnum AuthMode { get; set; } = CredentialAuthModeEnum.DirectHeader;

        /// <summary>Whether the credential is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Optional expiration.</summary>
        public DateTime? ExpiresUtc { get; set; } = null;

        /// <summary>Creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; }

        #endregion

        #region Public-Methods

        /// <summary>Build a response from a credential model, optionally including the raw secret.</summary>
        /// <param name="credential">Credential model.</param>
        /// <param name="rawSecret">Raw secret to include (create only), or null.</param>
        /// <returns>Credential response.</returns>
        public static CredentialResponse FromModel(Credential credential, string? rawSecret)
        {
            return new CredentialResponse
            {
                Id = credential.Id,
                TenantId = credential.TenantId,
                UserId = credential.UserId,
                Name = credential.Name,
                AccessKey = credential.AccessKey,
                SecretKey = rawSecret,
                SecretKeyLast4 = credential.SecretKeyLast4,
                AuthMode = credential.AuthMode,
                Active = credential.Active,
                ExpiresUtc = credential.ExpiresUtc,
                CreatedUtc = credential.CreatedUtc
            };
        }

        #endregion
    }
}
