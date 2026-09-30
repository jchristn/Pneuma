namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>What committing a wizard draft created.</summary>
    public class WizardCommitResult
    {
        /// <summary>The new subject.</summary>
        public Subject? Subject { get; set; } = null;

        /// <summary>Its starter questions.</summary>
        public List<SubjectQuestion> Questions { get; set; } = new List<SubjectQuestion>();

        /// <summary>How the ontology was saved.</summary>
        public WizardOntologyModeEnum OntologyMode { get; set; } = WizardOntologyModeEnum.Prompt;

        /// <summary>The tenant ontology created, if any.</summary>
        public string? OntologyId { get; set; } = null;

        /// <summary>The ontology version created, if any.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>The ontology as the classifier sees it.</summary>
        public string? RenderedOntology { get; set; } = null;

        /// <summary>What did not go as asked.</summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
