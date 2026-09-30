namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A node field a required-field rule checks.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyNodeFieldEnum
    {
        /// <summary>Content.</summary>
        Content,
        /// <summary>Rights classification.</summary>
        Rights,
        /// <summary>Authority classification.</summary>
        Authority,
        /// <summary>Canonical name.</summary>
        CanonicalName
    }
}
