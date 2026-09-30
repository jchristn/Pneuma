namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>Whether a graph element is a node or an edge.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyElementKindEnum
    {
        /// <summary>A node.</summary>
        Node,
        /// <summary>An edge.</summary>
        Edge
    }
}
