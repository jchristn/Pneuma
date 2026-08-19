namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// Request to create a credential.
    /// </summary>
    public class CreateCredentialRequest
    {
        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Owning user identifier. Defaults to the caller when omitted.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Optional UTC expiration.</summary>
        public DateTime? ExpiresUtc { get; set; } = null;
    }
}
