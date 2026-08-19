namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Request to create a user.
    /// </summary>
    public class CreateUserRequest
    {
        /// <summary>Tenant identifier. Defaults to the caller's tenant when omitted.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>First name.</summary>
        public string FirstName { get; set; } = String.Empty;

        /// <summary>Last name.</summary>
        public string LastName { get; set; } = String.Empty;

        /// <summary>Email address (unique within tenant).</summary>
        public string Email { get; set; } = String.Empty;

        /// <summary>Plaintext password (hashed server-side).</summary>
        public string Password { get; set; } = String.Empty;

        /// <summary>Whether the user is a global administrator.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>Whether the user is a tenant administrator.</summary>
        public bool IsTenantAdmin { get; set; } = false;
    }
}
