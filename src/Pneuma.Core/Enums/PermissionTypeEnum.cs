namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Whether a permission grants or explicitly denies access. Deny always wins.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PermissionTypeEnum
    {
        /// <summary>Grants the described access.</summary>
        Permit,
        /// <summary>Explicitly denies the described access; overrides any permit.</summary>
        Deny
    }
}
