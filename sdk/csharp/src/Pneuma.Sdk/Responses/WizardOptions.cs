namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>What the new subject wizard can do for the caller.</summary>
    public class WizardOptions
    {
        /// <summary>Ontology modes the caller may use.</summary>
        public List<WizardOntologyModeEnum> OntologyModes { get; set; } = new List<WizardOntologyModeEnum>();

        /// <summary>Mode to preselect.</summary>
        public WizardOntologyModeEnum DefaultOntologyMode { get; set; } = WizardOntologyModeEnum.Prompt;

        /// <summary>Questions drafted by default.</summary>
        public int DefaultQuestionCount { get; set; } = 12;

        /// <summary>Most questions a draft can hold.</summary>
        public int MaxQuestions { get; set; } = 40;

        /// <summary>Most questions the coverage check asks.</summary>
        public int CoverageMaxQuestions { get; set; } = 12;

        /// <summary>True when reference URLs can be read.</summary>
        public bool GroundingUrlEnabled { get; set; } = true;

        /// <summary>True when the tenant has an active completion endpoint.</summary>
        public bool HasCompletionModel { get; set; } = false;
    }
}
