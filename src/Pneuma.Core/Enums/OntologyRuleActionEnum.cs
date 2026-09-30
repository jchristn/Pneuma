namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What happens to an element that breaks an ontology rule.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyRuleActionEnum
    {
        /// <summary>Keep the element and record a warning.</summary>
        Warn,
        /// <summary>Leave the element out of the graph and record it.</summary>
        Drop,
        /// <summary>Hold the element out of the graph for review; it can be released into the graph or dismissed.</summary>
        Quarantine,
        /// <summary>For edge endpoint rules only: reverse the edge when the reversed direction satisfies the rule; otherwise drop it.</summary>
        Reverse
    }
}
