namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>Everything a new subject wizard has drafted so far. The browser holds it; each step sends it back.</summary>
    public class SubjectWizardDraft
    {
        #region Public-Members

        /// <summary>What the subject is and who will ask about it, in the user's words. Required.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Optional text about the subject for the model to read (pasted, or the excerpt returned from a grounding URL).</summary>
        public string? GroundingText { get; set; } = null;

        /// <summary>Optional web page about the subject; the brief step fetches it and returns an excerpt to keep as grounding text.</summary>
        public string? GroundingUrl { get; set; } = null;

        /// <summary>The brief, once drafted.</summary>
        public WizardBrief? Brief { get; set; } = null;

        /// <summary>The example questions so far.</summary>
        public List<WizardQuestion> Questions { get; set; } = new List<WizardQuestion>();

        /// <summary>The ontology, once drafted.</summary>
        public WizardOntology? Ontology { get; set; } = null;

        /// <summary>The prompt additions, once drafted.</summary>
        public WizardPrompts? Prompts { get; set; } = null;

        #endregion
    }
}
