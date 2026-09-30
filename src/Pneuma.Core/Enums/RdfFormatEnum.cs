namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>An RDF serialization used for ontology export and taxonomy import.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum RdfFormatEnum
    {
        /// <summary>Turtle.</summary>
        Turtle,
        /// <summary>JSON-LD.</summary>
        JsonLd
    }
}
