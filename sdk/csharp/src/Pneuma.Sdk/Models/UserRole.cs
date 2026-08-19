namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A role: a named container of permissions. Built-in roles have a null tenant and are globally visible.
    /// </summary>
    public class UserRole
    {
        /// <summary>Role identifier (prefix "rol_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier. Null for built-in roles.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Human-readable role name.</summary>
        public string Name { get; set; } = string.Empty;

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
    }
}
