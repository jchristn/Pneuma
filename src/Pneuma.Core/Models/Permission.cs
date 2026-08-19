namespace Pneuma.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A granular permission describing which operations are permitted or denied on which resource types.
    /// </summary>
    public class Permission
    {
        #region Public-Members

        /// <summary>Permission identifier (prefix "per_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier. Null for built-in permissions.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Human-readable permission name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Resource types this permission applies to. Use All for wildcard.</summary>
        public List<ResourceTypeEnum> ResourceTypes { get; set; } = new List<ResourceTypeEnum>();

        /// <summary>Operation types this permission covers. Use All for wildcard.</summary>
        public List<OperationTypeEnum> OperationTypes { get; set; } = new List<OperationTypeEnum>();

        /// <summary>Whether this permission permits or explicitly denies.</summary>
        public PermissionTypeEnum PermissionType { get; set; } = PermissionTypeEnum.Permit;

        /// <summary>Whether the permission is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the permission is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GeneratePermissionId();

        #endregion
    }
}
