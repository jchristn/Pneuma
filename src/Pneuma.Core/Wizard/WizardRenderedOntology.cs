namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>A wizard ontology draft cleaned up and rendered as the classifier will see it.</summary>
    public class WizardRenderedOntology
    {
        #region Public-Members

        /// <summary>The cleaned-up draft.</summary>
        public WizardOntology Ontology { get; set; } = new WizardOntology();

        /// <summary>The rendered definition.</summary>
        public string Rendered { get; set; } = String.Empty;

        /// <summary>What the clean-up changed or dropped.</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        #endregion
    }
}
