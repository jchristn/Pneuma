namespace Pneuma.Core.Wizard
{
    using System.Text.Json.Serialization;

    /// <summary>How the new subject wizard saves the ontology it drafted.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum WizardOntologyModeEnum
    {
        /// <summary>As the subject's ontology definition prompt (no governed ontology is created).</summary>
        Prompt,
        /// <summary>As a draft version of a new tenant ontology, left for an approver; the prompt is used meanwhile.</summary>
        Draft,
        /// <summary>As a new tenant ontology whose first version is approved and pinned to the subject (needs Ontology Execute).</summary>
        Approve
    }
}
