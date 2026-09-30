namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The lifecycle state of an ontology version.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyVersionStatusEnum
    {
        /// <summary>Editable.</summary>
        Draft,
        /// <summary>Immutable and pinnable.</summary>
        Approved,
        /// <summary>Immutable and not pinnable.</summary>
        Retired
    }
}
