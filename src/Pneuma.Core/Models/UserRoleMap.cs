namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// Legacy tenant-wide user-to-role mapping, retained for backward compatibility.
    /// Prefer <see cref="UserRoleAssignment"/> for new work.
    /// </summary>
    public class UserRoleMap
    {
        #region Public-Members

        /// <summary>Mapping identifier (prefix "asn_").</summary>
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

        /// <summary>User identifier.</summary>
        public string UserId
        {
            get { return _UserId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(UserId)); _UserId = value; }
        }

        /// <summary>Role identifier.</summary>
        public string RoleId
        {
            get { return _RoleId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RoleId)); _RoleId = value; }
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
        private string _TenantId = String.Empty;
        private string _UserId = String.Empty;
        private string _RoleId = String.Empty;

        #endregion
    }
}
