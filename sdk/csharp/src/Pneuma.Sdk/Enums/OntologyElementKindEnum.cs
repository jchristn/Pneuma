namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Node or edge.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyElementKindEnum
    {
        /// <summary>A node.</summary>
        Node,
        /// <summary>An edge.</summary>
        Edge
    }
}
