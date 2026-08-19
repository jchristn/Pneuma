namespace Pneuma.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// Binds a credential to a role or a direct permission set at a tenant or resource scope.
    /// Mirrors <see cref="UserRoleAssignment"/> but keyed on a credential.
    /// </summary>
    public class CredentialScopeAssignment
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

        /// <summary>Credential identifier.</summary>
        public string CredentialId
        {
            get { return _CredentialId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(CredentialId)); _CredentialId = value; }
        }

        /// <summary>Optional role identifier reference.</summary>
        public string? RoleId { get; set; } = null;

        /// <summary>Optional built-in role name reference.</summary>
        public string? RoleName { get; set; } = null;

        /// <summary>Scope of the grant.</summary>
        public ResourceScopeEnum ResourceScope { get; set; } = ResourceScopeEnum.Tenant;

        /// <summary>Target resource identifier when resource-scoped.</summary>
        public string? ResourceId { get; set; } = null;

        /// <summary>Optional direct permission list for role-less grants.</summary>
        public List<OperationTypeEnum> Permissions { get; set; } = new List<OperationTypeEnum>();

        /// <summary>Optional direct resource-type list for role-less grants.</summary>
        public List<ResourceTypeEnum> ResourceTypes { get; set; } = new List<ResourceTypeEnum>();

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
        private string _CredentialId = String.Empty;

        #endregion
    }
}
