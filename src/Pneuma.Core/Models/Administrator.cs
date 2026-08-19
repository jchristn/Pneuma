namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// System-level administrator. Administrators bypass tenant-scoped authorization.
    /// </summary>
    public class Administrator
    {
        #region Public-Members

        /// <summary>Administrator identifier (prefix "adm_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning account identifier, if any.</summary>
        public string? AccountId { get; set; } = null;

        /// <summary>First name.</summary>
        public string FirstName { get; set; } = String.Empty;

        /// <summary>Last name.</summary>
        public string LastName { get; set; } = String.Empty;

        /// <summary>Email address (used for login).</summary>
        public string Email
        {
            get { return _Email; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Email)); _Email = value; }
        }

        /// <summary>SHA-256 hash of the password (hex encoded).</summary>
        public string PasswordSha256 { get; set; } = String.Empty;

        /// <summary>Optional telephone number.</summary>
        public string? Telephone { get; set; } = null;

        /// <summary>Whether the administrator is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the administrator is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateAdminId();
        private string _Email = String.Empty;

        #endregion
    }
}
