namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Review state of a violation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyViolationStatusEnum
    {
        /// <summary>Applied; nothing pending.</summary>
        Recorded,
        /// <summary>Held for review.</summary>
        Quarantined,
        /// <summary>Released into the graph.</summary>
        Released,
        /// <summary>Dismissed.</summary>
        Dismissed
    }
}
