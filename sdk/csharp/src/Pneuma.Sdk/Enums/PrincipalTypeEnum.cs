namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The kind of principal an authenticated request resolves to.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PrincipalTypeEnum
    {
        /// <summary>System administrator.</summary>
        Administrator,
        /// <summary>Interactive user.</summary>
        User,
        /// <summary>Non-interactive credential (API key).</summary>
        Credential
    }
}
