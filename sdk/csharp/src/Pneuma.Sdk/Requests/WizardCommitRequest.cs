namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>A request to create a subject from a finished wizard draft.</summary>
    public class WizardCommitRequest
    {
        /// <summary>The finished draft.</summary>
        public SubjectWizardDraft Draft { get; set; } = new SubjectWizardDraft();

        /// <summary>Completion model endpoint; null for the first active one.</summary>
        public string? InferenceModel { get; set; } = null;

        /// <summary>Embedding model endpoint; null for the first active one.</summary>
        public string? EmbeddingModel { get; set; } = null;

        /// <summary>Collection; null for the default.</summary>
        public string? Collection { get; set; } = null;

        /// <summary>How to save the ontology (lowered to what the caller may do).</summary>
        public WizardOntologyModeEnum OntologyMode { get; set; } = WizardOntologyModeEnum.Approve;

        /// <summary>Show the subject in the consumer chat.</summary>
        public bool PublishedForChat { get; set; } = true;
    }
}
