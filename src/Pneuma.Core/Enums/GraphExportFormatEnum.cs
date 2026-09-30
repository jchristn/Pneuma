namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A format a subject's knowledge graph can be exported in.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum GraphExportFormatEnum
    {
        /// <summary>Pneuma JSON: nodes and edges with their tags.</summary>
        Json,
        /// <summary>JSON-LD (RDF), with PROV-O provenance.</summary>
        JsonLd,
        /// <summary>Turtle (RDF), with PROV-O provenance.</summary>
        Turtle,
        /// <summary>GraphML, for graph tools such as Gephi and yEd.</summary>
        GraphMl
    }
}
