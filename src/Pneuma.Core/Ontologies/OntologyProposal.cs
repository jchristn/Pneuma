namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>The authoring assistant's proposal: the JSON shape the <c>ontology.propose.format</c> prompt asks the model for.</summary>
    public class OntologyProposal
    {
        #region Public-Members

        /// <summary>Proposed node types.</summary>
        public List<OntologyProposalType> NodeTypes { get; set; } = new List<OntologyProposalType>();

        /// <summary>Proposed edge types.</summary>
        public List<OntologyProposalType> EdgeTypes { get; set; } = new List<OntologyProposalType>();

        /// <summary>Proposed allowed endpoints of the edge types.</summary>
        public List<OntologyProposalEndpoint> EdgeEndpoints { get; set; } = new List<OntologyProposalEndpoint>();

        /// <summary>Proposed extraction guidance.</summary>
        public string? Guidance { get; set; } = null;

        /// <summary>What the proposal changes, in words.</summary>
        public string? ChangeSummary { get; set; } = null;

        #endregion
    }
}
