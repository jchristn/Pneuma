namespace Pneuma.Core.Database
{
    using Pneuma.Core.Models;

    /// <summary>
    /// The outcome of provisioning a tenant's first administrator: the admin user plus any records that
    /// were newly created. The raw <see cref="SecretKey"/> is populated only when a default credential was
    /// created on this call (it is never persisted in the clear and cannot be recovered later); it is null
    /// when the credential already existed.
    /// </summary>
    public class TenantProvisionResult
    {
        #region Public-Members

        /// <summary>The tenant administrator user (existing or newly created).</summary>
        public User User { get; set; } = new User();

        /// <summary>Whether the admin user was created on this call.</summary>
        public bool UserCreated { get; set; }

        /// <summary>Whether the TenantAdmin role assignment was created on this call.</summary>
        public bool AssignmentCreated { get; set; }

        /// <summary>Whether the default API-key credential was created on this call.</summary>
        public bool CredentialCreated { get; set; }

        /// <summary>Public access key of the default credential, when one was created; otherwise null.</summary>
        public string? AccessKey { get; set; }

        /// <summary>Raw secret key of the default credential, shown only when created; otherwise null.</summary>
        public string? SecretKey { get; set; }

        #endregion
    }
}
