namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The ontology step's model output: the JSON shape the <c>wizard.ontology.format</c> prompt asks for.</summary>
    public class WizardOntologyOutput
    {
        #region Public-Members

        /// <summary>Proposed node types.</summary>
        public List<WizardTypeOutput> NodeTypes { get; set; } = new List<WizardTypeOutput>();

        /// <summary>Proposed relationship types.</summary>
        public List<WizardTypeOutput> EdgeTypes { get; set; } = new List<WizardTypeOutput>();

        /// <summary>Proposed extraction guidance.</summary>
        public string? Guidance { get; set; } = null;

        #endregion
    }
}
