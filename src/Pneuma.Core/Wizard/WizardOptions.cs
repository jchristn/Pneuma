namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>What the new subject wizard can do for the caller, and its limits.</summary>
    public class WizardOptions
    {
        #region Public-Members

        /// <summary>Ontology modes the caller may use.</summary>
        public List<WizardOntologyModeEnum> OntologyModes { get; set; } = new List<WizardOntologyModeEnum>();

        /// <summary>The mode the wizard should preselect.</summary>
        public WizardOntologyModeEnum DefaultOntologyMode { get; set; } = WizardOntologyModeEnum.Prompt;

        /// <summary>Questions drafted by default.</summary>
        public int DefaultQuestionCount { get; set; } = 12;

        /// <summary>Most questions a draft can hold.</summary>
        public int MaxQuestions { get; set; } = 40;

        /// <summary>Most questions the coverage check asks.</summary>
        public int CoverageMaxQuestions { get; set; } = 12;

        /// <summary>Most reference URLs the brief step reads.</summary>
        public int MaxGroundingUrls { get; set; } = 5;

        /// <summary>True when a reference URL can be read for grounding.</summary>
        public bool GroundingUrlEnabled { get; set; } = true;

        /// <summary>True when the tenant has an active completion endpoint (the wizard cannot draft without one).</summary>
        public bool HasCompletionModel { get; set; } = false;

        #endregion
    }
}
