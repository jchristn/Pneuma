namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// Binds a user to a role at a tenant or resource scope. The authoritative RBAC assignment record.
    /// </summary>
    public class UserRoleAssignment
    {
        /// <summary>Assignment identifier (prefix "asn_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>User identifier.</summary>
        public string UserId { get; set; } = string.Empty;

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
    }
}
