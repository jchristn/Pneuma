namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a credential authenticates.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum CredentialAuthModeEnum
    {
        /// <summary>Access key and secret key sent directly in headers.</summary>
        DirectHeader,
        /// <summary>Access key plus an HMAC request signature.</summary>
        SignedRequest,
        /// <summary>Exchange stronger credentials for a short-lived session token.</summary>
        SessionExchange,
        /// <summary>Supports more than one of the above modes.</summary>
        Hybrid
    }
}
