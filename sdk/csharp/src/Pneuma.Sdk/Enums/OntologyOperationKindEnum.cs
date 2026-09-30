namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A background ontology operation.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyOperationKindEnum
    {
        /// <summary>Check the stored graph against the rules.</summary>
        Validate,
        /// <summary>Re-link cells to the taxonomy.</summary>
        Retag,
        /// <summary>Measure classification drift.</summary>
        DriftCheck
    }
}
