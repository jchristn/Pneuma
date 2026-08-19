namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of an authentication attempt.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthenticationResultEnum
    {
        /// <summary>Authentication succeeded.</summary>
        Success,
        /// <summary>The referenced principal or credential was not found.</summary>
        NotFound,
        /// <summary>The principal, credential, tenant, or session is inactive.</summary>
        Inactive,
        /// <summary>The supplied material was invalid.</summary>
        Invalid
    }
}
