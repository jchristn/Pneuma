namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What happens to a node or edge whose type the pinned ontology version does not declare.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum UndeclaredTypeActionEnum
    {
        /// <summary>Keep it (the model may extend the ontology).</summary>
        Allow,
        /// <summary>Keep it and record a warning.</summary>
        Warn,
        /// <summary>Leave it out of the graph and record it.</summary>
        Drop,
        /// <summary>Hold it out of the graph for review.</summary>
        Quarantine
    }
}
