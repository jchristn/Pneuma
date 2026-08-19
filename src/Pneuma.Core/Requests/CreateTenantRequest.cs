namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Request to create a tenant. Beyond the tenant fields, an optional first administrator may be
    /// specified; when omitted, a tenant administrator is still created with a derived email and a
    /// generated password (both surfaced once in the response) so the new tenant is immediately usable.
    /// </summary>
    public class CreateTenantRequest
    {
        #region Public-Members

        /// <summary>Tenant name (required).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Owning account identifier, if any.</summary>
        public string? AccountId { get; set; } = null;

        /// <summary>Parent tenant identifier, if any.</summary>
        public string? ParentId { get; set; } = null;

        /// <summary>Region, if any.</summary>
        public string? Region { get; set; } = null;

        /// <summary>Whether the tenant is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Optional first-admin email. When omitted, a derived email is used.</summary>
        public string? AdminEmail { get; set; } = null;

        /// <summary>Optional first-admin password. When omitted, a password is generated and returned once.</summary>
        public string? AdminPassword { get; set; } = null;

        /// <summary>Optional first-admin first name.</summary>
        public string? AdminFirstName { get; set; } = null;

        /// <summary>Optional first-admin last name.</summary>
        public string? AdminLastName { get; set; } = null;

        #endregion
    }
}
