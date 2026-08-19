namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A tenant: the primary isolation boundary for users, credentials, and subject archives.
    /// </summary>
    public class Tenant
    {
        #region Public-Members

        /// <summary>Tenant identifier (prefix "ten_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning account identifier, if any.</summary>
        public string? AccountId { get; set; } = null;

        /// <summary>Optional parent tenant identifier for hierarchical tenants.</summary>
        public string? ParentId { get; set; } = null;

        /// <summary>Human-readable tenant name.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value; }
        }

        /// <summary>Optional geographic region label.</summary>
        public string? Region { get; set; } = null;

        /// <summary>Whether the tenant is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the tenant is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateTenantId();
        private string _Name = String.Empty;

        #endregion
    }
}
