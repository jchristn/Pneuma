namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A tenant-scoped user. Email is unique within a tenant. Passwords are never returned.
    /// </summary>
    public class User
    {
        /// <summary>User identifier (prefix "usr_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>First name.</summary>
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Last name.</summary>
        public string LastName { get; set; } = string.Empty;

        /// <summary>Email address (unique within tenant, used for login).</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>If true, the user has system-wide administrative access, bypassing all checks.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>If true, the user has full access within their tenant, bypassing RBAC permission checks.</summary>
        public bool IsTenantAdmin { get; set; } = false;

        /// <summary>Whether the user is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the user is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
