namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>A built-in ontology template a tenant can create an ontology from.</summary>
    public class OntologyTemplateInfo
    {
        #region Public-Members

        /// <summary>Template name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>What the template contains.</summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>Number of node types.</summary>
        public int NodeTypeCount { get; set; } = 0;

        /// <summary>Number of edge types.</summary>
        public int EdgeTypeCount { get; set; } = 0;

        /// <summary>Number of rules.</summary>
        public int RuleCount { get; set; } = 0;

        #endregion
    }
}
