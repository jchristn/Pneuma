namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>A background ontology operation on a subject.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum OntologyOperationKindEnum
    {
        /// <summary>Check the subject's stored graph against its pinned ontology version's rules.</summary>
        Validate,
        /// <summary>Re-apply the pinned version's taxonomy to the subject's stored cells.</summary>
        Retag,
        /// <summary>Classify a sample of the subject's cells twice and report how often the result changes.</summary>
        DriftCheck
    }
}
