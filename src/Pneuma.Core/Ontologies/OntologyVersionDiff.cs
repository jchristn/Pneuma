namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>What changed between two ontology versions (from the older to the newer).</summary>
    public class OntologyVersionDiff
    {
        #region Public-Members

        /// <summary>The version compared from.</summary>
        public string FromVersionId { get; set; } = String.Empty;

        /// <summary>The version compared to.</summary>
        public string ToVersionId { get; set; } = String.Empty;

        /// <summary>Node types only in the newer version.</summary>
        public List<string> AddedNodeTypes { get; set; } = new List<string>();

        /// <summary>Node types only in the older version.</summary>
        public List<string> RemovedNodeTypes { get; set; } = new List<string>();

        /// <summary>Node types in both whose description changed.</summary>
        public List<string> ChangedNodeTypes { get; set; } = new List<string>();

        /// <summary>Edge types only in the newer version.</summary>
        public List<string> AddedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Edge types only in the older version.</summary>
        public List<string> RemovedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Edge types in both whose description changed.</summary>
        public List<string> ChangedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Rules only in the newer version, described in words.</summary>
        public List<string> AddedRules { get; set; } = new List<string>();

        /// <summary>Rules only in the older version, described in words.</summary>
        public List<string> RemovedRules { get; set; } = new List<string>();

        /// <summary>Concept keys only in the newer version.</summary>
        public List<string> AddedConcepts { get; set; } = new List<string>();

        /// <summary>Concept keys only in the older version.</summary>
        public List<string> RemovedConcepts { get; set; } = new List<string>();

        /// <summary>Concept keys in both whose labels, broader concept, node type, definition, or case sensitivity changed.</summary>
        public List<string> ChangedConcepts { get; set; } = new List<string>();

        /// <summary>Whether the guidance text changed.</summary>
        public bool GuidanceChanged { get; set; } = false;

        /// <summary>Whether the undeclared-type action changed.</summary>
        public bool UndeclaredTypeActionChanged { get; set; } = false;

        /// <summary>Whether anything that shapes tagging (the concepts) changed.</summary>
        public bool TaxonomyChanged
        {
            get { return AddedConcepts.Count > 0 || RemovedConcepts.Count > 0 || ChangedConcepts.Count > 0; }
        }

        #endregion
    }
}
