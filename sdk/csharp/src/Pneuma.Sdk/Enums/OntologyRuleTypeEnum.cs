namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What an ontology rule checks.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyRuleTypeEnum
    {
        /// <summary>Allowed source and target node types of an edge type.</summary>
        EdgeEndpoints,
        /// <summary>At most a number of outgoing edges of a type.</summary>
        MaxOutgoing,
        /// <summary>A node field must have a value.</summary>
        RequiredField,
        /// <summary>A node name must match a regular expression.</summary>
        NamePattern,
        /// <summary>A minimum model confidence.</summary>
        MinConfidence
    }
}
