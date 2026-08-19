namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Breadth of a role or credential assignment.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResourceScopeEnum
    {
        /// <summary>Applies across the entire tenant.</summary>
        Tenant,
        /// <summary>Applies only to a specific resource GUID.</summary>
        Resource
    }
}
