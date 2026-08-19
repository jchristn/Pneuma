namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// Many-to-many association between a role and a permission.
    /// </summary>
    public class RolePermissionMap
    {
        #region Public-Members

        /// <summary>Mapping identifier (prefix "asn_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier. Null for built-in mappings.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Role identifier.</summary>
        public string RoleId
        {
            get { return _RoleId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RoleId)); _RoleId = value; }
        }

        /// <summary>Permission identifier.</summary>
        public string PermissionId
        {
            get { return _PermissionId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(PermissionId)); _PermissionId = value; }
        }

        /// <summary>Whether the mapping is in effect.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the mapping is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateAssignmentId();
        private string _RoleId = String.Empty;
        private string _PermissionId = String.Empty;

        #endregion
    }
}
