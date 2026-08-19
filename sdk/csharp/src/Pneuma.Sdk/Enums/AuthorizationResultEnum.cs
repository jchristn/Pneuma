namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Outcome of an authorization evaluation.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthorizationResultEnum
    {
        /// <summary>Access permitted.</summary>
        Permitted,
        /// <summary>Access denied by an explicit deny rule.</summary>
        DeniedExplicit,
        /// <summary>Access denied because no rule matched.</summary>
        DeniedImplicit,
        /// <summary>The target resource was not found.</summary>
        NotFound,
        /// <summary>A conflict prevented evaluation.</summary>
        Conflict
    }
}
