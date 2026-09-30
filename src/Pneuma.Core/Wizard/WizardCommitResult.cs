namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Models;

    /// <summary>What committing a wizard draft created.</summary>
    public class WizardCommitResult
    {
        #region Public-Members

        /// <summary>The new subject.</summary>
        public Subject? Subject { get; set; } = null;

        /// <summary>The subject's starter questions.</summary>
        public List<SubjectQuestion> Questions { get; set; } = new List<SubjectQuestion>();

        /// <summary>How the ontology was saved (after any lowering for permissions).</summary>
        public WizardOntologyModeEnum OntologyMode { get; set; } = WizardOntologyModeEnum.Prompt;

        /// <summary>The tenant ontology created, when the mode is Draft or Approve.</summary>
        public string? OntologyId { get; set; } = null;

        /// <summary>The ontology version created, when the mode is Draft or Approve.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>The ontology as the classifier sees it.</summary>
        public string? RenderedOntology { get; set; } = null;

        /// <summary>Things that did not go as asked (for example a mode lowered for permissions).</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        #endregion
    }
}
