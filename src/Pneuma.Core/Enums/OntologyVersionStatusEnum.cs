namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>The lifecycle state of an ontology version.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyVersionStatusEnum
    {
        /// <summary>Editable; not yet usable by subjects.</summary>
        Draft,
        /// <summary>Immutable and available for subjects to pin.</summary>
        Approved,
        /// <summary>Immutable and no longer available for new pins.</summary>
        Retired
    }
}
