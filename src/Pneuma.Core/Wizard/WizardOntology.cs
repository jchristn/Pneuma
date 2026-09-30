namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The ontology in a wizard draft: node types, relationship types, and extraction guidance.</summary>
    public class WizardOntology
    {
        #region Public-Members

        /// <summary>Node types.</summary>
        public List<WizardNodeType> NodeTypes { get; set; } = new List<WizardNodeType>();

        /// <summary>Relationship types.</summary>
        public List<WizardEdgeType> EdgeTypes { get; set; } = new List<WizardEdgeType>();

        /// <summary>Extraction guidance for the classifier.</summary>
        public string? Guidance { get; set; } = null;

        #endregion
    }
}
