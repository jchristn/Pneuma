namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Database;
    using Pneuma.Core.Models;

    /// <summary>
    /// Response for tenant creation: the created tenant plus the first administrator that was provisioned
    /// for it. The <see cref="AdminPassword"/> is present only when the password was generated (not supplied
    /// by the caller), and <see cref="SecretKey"/> is present only when the default API key was created on
    /// this call. Both are shown once and cannot be recovered later.
    /// </summary>
    public class TenantProvisionResponse
    {
        #region Public-Members

        /// <summary>The created tenant.</summary>
        public Tenant Tenant { get; set; } = new Tenant();

        /// <summary>The provisioned administrator user identifier.</summary>
        public string AdminUserId { get; set; } = string.Empty;

        /// <summary>The administrator email (derived when not supplied).</summary>
        public string AdminEmail { get; set; } = string.Empty;

        /// <summary>The generated administrator password, shown once; null when the caller supplied one.</summary>
        public string? AdminPassword { get; set; } = null;

        /// <summary>The default credential access key, when a credential was created; otherwise null.</summary>
        public string? AccessKey { get; set; } = null;

        /// <summary>The default credential raw secret key, shown once; null when no credential was created.</summary>
        public string? SecretKey { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>Build a response from a provisioning result.</summary>
        /// <param name="tenant">The created tenant.</param>
        /// <param name="result">The provisioning result.</param>
        /// <param name="adminEmail">The administrator email that was used.</param>
        /// <param name="generatedPassword">The generated password, or null when the caller supplied one.</param>
        /// <returns>The response DTO.</returns>
        public static TenantProvisionResponse FromResult(Tenant tenant, TenantProvisionResult result, string adminEmail, string? generatedPassword)
        {
            return new TenantProvisionResponse
            {
                Tenant = tenant,
                AdminUserId = result.User.Id,
                AdminEmail = adminEmail,
                AdminPassword = generatedPassword,
                AccessKey = result.AccessKey,
                SecretKey = result.SecretKey
            };
        }

        #endregion
    }
}
