namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A tenant-scoped user. Email is unique within a tenant.
    /// </summary>
    public class User
    {
        #region Public-Members

        /// <summary>User identifier (prefix "usr_").</summary>
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

        /// <summary>First name.</summary>
        public string FirstName { get; set; } = String.Empty;

        /// <summary>Last name.</summary>
        public string LastName { get; set; } = String.Empty;

        /// <summary>Email address (unique within tenant, used for login).</summary>
        public string Email
        {
            get { return _Email; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Email)); _Email = value; }
        }

        /// <summary>SHA-256 hash of the password (hex encoded).</summary>
        public string PasswordSha256 { get; set; } = String.Empty;

        /// <summary>If true, the user has system-wide administrative access (Pneuma IsSystemAdmin), bypassing all checks.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>If true, the user has full access within their tenant, bypassing RBAC permission checks.</summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>Whether the user is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the user is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateUserId();
        private string _TenantId = String.Empty;
        private string _Email = String.Empty;

        #endregion
    }
}
