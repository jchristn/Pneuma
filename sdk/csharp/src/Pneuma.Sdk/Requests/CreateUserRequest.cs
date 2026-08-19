namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>
    /// Request to create or update a user.
    /// </summary>
    public class CreateUserRequest
    {
        /// <summary>Tenant identifier. Defaults to the caller's tenant when omitted.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>First name.</summary>
        public string FirstName { get; set; } = string.Empty;

        /// <summary>Last name.</summary>
        public string LastName { get; set; } = string.Empty;

        /// <summary>Email address (unique within tenant).</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Plaintext password (hashed server-side).</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Whether the user is a global administrator.</summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>Whether the user is a tenant administrator.</summary>
        public bool IsTenantAdmin { get; set; } = false;
    }
}
