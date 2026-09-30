namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The state of a recorded ontology rule violation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyViolationStatusEnum
    {
        /// <summary>The rule's action was applied (warned, dropped, or reversed); nothing is pending.</summary>
        Recorded,
        /// <summary>The element is held out of the graph for review.</summary>
        Quarantined,
        /// <summary>A reviewer released the quarantined element into the graph.</summary>
        Released,
        /// <summary>A reviewer dismissed the quarantined element.</summary>
        Dismissed
    }
}
