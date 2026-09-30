namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A field of a classified node that a required-field rule can check.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyNodeFieldEnum
    {
        /// <summary>The node's free-text content.</summary>
        Content,
        /// <summary>The node's rights classification.</summary>
        Rights,
        /// <summary>The node's authority classification.</summary>
        Authority,
        /// <summary>The node's canonical name (a model-supplied canonical form of its name).</summary>
        CanonicalName
    }
}
