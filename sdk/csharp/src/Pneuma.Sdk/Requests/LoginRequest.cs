namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>
    /// Email/password login request used to create a session token.
    /// </summary>
    public class LoginRequest
    {
        /// <summary>Email address.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Plaintext password.</summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>Optional tenant identifier. When omitted, the email is resolved across tenants.</summary>
        public string? TenantId { get; set; } = null;
    }
}
