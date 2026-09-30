namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The ontology in a wizard draft.</summary>
    public class WizardOntology
    {
        /// <summary>Node types.</summary>
        public List<WizardNodeType> NodeTypes { get; set; } = new List<WizardNodeType>();

        /// <summary>Relationship types.</summary>
        public List<WizardEdgeType> EdgeTypes { get; set; } = new List<WizardEdgeType>();

        /// <summary>Extraction guidance.</summary>
        public string? Guidance { get; set; } = null;
    }
}
