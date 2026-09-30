namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>How the new subject wizard saves its ontology.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WizardOntologyModeEnum
    {
        /// <summary>As the subject's ontology definition prompt.</summary>
        Prompt,
        /// <summary>As a draft version of a new tenant ontology.</summary>
        Draft,
        /// <summary>As a new tenant ontology, approved and pinned to the subject.</summary>
        Approve
    }
}
