namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>A node or edge type in an authoring proposal.</summary>
    public class OntologyProposalType
    {
        #region Public-Members

        /// <summary>Type name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>One-sentence description.</summary>
        public string? Description { get; set; } = null;

        #endregion
    }
}
