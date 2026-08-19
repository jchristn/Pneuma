namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// Binds a user to a role at a tenant or resource scope. The authoritative RBAC assignment record.
    /// </summary>
    public class UserRoleAssignment
    {
        #region Public-Members

        /// <summary>Assignment identifier (prefix "asn_").</summary>
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

        /// <summary>Role identifier reference. Nullable when a built-in role name is used instead.</summary>
        public string? RoleId { get; set; } = null;

        /// <summary>Built-in role name reference. Nullable when a role identifier is used instead.</summary>
        public string? RoleName { get; set; } = null;

        /// <summary>Scope of the grant.</summary>
        public ResourceScopeEnum ResourceScope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>Target resource identifier when resource-scoped.</summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>Whether a tenant-scoped grant flows to child resources.</summary>
        public bool InheritsToChildren { get; set; } = true;

        /// <summary>Whether the assignment is in effect.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the assignment is protected from deletion.</summary>
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

        #endregion
    }
}
