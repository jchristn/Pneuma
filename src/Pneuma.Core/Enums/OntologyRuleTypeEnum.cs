namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>What an ontology constraint rule checks.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyRuleTypeEnum
    {
        /// <summary>An edge type may only connect the listed source and target node types. Several rules for one edge type are alternatives.</summary>
        EdgeEndpoints,
        /// <summary>A node of a type may have at most a number of outgoing edges of an edge type.</summary>
        MaxOutgoing,
        /// <summary>A node of a type must have a value in a field (content, rights, authority, or canonical name).</summary>
        RequiredField,
        /// <summary>A node of a type must have a name that matches a regular expression.</summary>
        NamePattern,
        /// <summary>A node or edge of a type must have at least a model confidence.</summary>
        MinConfidence
    }
}
