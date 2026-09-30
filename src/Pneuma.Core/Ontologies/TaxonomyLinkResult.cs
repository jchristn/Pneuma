namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>How many taxonomy links (cell ABOUT concept) a linking pass added and removed.</summary>
    public class TaxonomyLinkResult
    {
        #region Public-Members

        /// <summary>Links added.</summary>
        public int Added { get; set; } = 0;

        /// <summary>Links removed (retagging only).</summary>
        public int Removed { get; set; } = 0;

        /// <summary>Concept nodes created.</summary>
        public int ConceptNodesCreated { get; set; } = 0;

        #endregion
    }
}
