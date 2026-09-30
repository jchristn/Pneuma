namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>Everything a new subject wizard has drafted so far.</summary>
    public class SubjectWizardDraft
    {
        /// <summary>What the subject is and who will ask about it (required).</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Optional reference text.</summary>
        public string? GroundingText { get; set; } = null;

        /// <summary>Optional reference web page.</summary>
        public string? GroundingUrl { get; set; } = null;

        /// <summary>The brief.</summary>
        public WizardBrief? Brief { get; set; } = null;

        /// <summary>Example questions.</summary>
        public List<WizardQuestion> Questions { get; set; } = new List<WizardQuestion>();

        /// <summary>The ontology.</summary>
        public WizardOntology? Ontology { get; set; } = null;

        /// <summary>The prompt additions.</summary>
        public WizardPrompts? Prompts { get; set; } = null;
    }
}
