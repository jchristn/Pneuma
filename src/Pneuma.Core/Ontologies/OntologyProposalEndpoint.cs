namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>An allowed direction of an edge type in an authoring proposal.</summary>
    public class OntologyProposalEndpoint
    {
        #region Public-Members

        /// <summary>Edge type.</summary>
        public string? EdgeType { get; set; } = null;

        /// <summary>Source node type.</summary>
        public string? FromNodeType { get; set; } = null;

        /// <summary>Target node type.</summary>
        public string? ToNodeType { get; set; } = null;

        #endregion
    }
}
