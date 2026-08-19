namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A role: a named container of permissions. Built-in roles have a null tenant and are globally visible.
    /// </summary>
    public class UserRole
    {
        #region Public-Members

        /// <summary>Role identifier (prefix "rol_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier. Null for built-in roles.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Human-readable role name.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value; }
        }

        /// <summary>Whether the role is seeded by the platform.</summary>
        public bool IsBuiltIn { get; set; } = false;

        /// <summary>Whether the role is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the role is protected from deletion or mutation.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateRoleId();
        private string _Name = String.Empty;

        #endregion
    }
}
