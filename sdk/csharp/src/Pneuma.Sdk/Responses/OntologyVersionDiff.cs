namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>What changed between two ontology versions.</summary>
    public class OntologyVersionDiff
    {
        /// <summary>From version.</summary>
        public string FromVersionId { get; set; } = string.Empty;

        /// <summary>To version.</summary>
        public string ToVersionId { get; set; } = string.Empty;

        /// <summary>Added node types.</summary>
        public List<string> AddedNodeTypes { get; set; } = new List<string>();

        /// <summary>Removed node types.</summary>
        public List<string> RemovedNodeTypes { get; set; } = new List<string>();

        /// <summary>Changed node types.</summary>
        public List<string> ChangedNodeTypes { get; set; } = new List<string>();

        /// <summary>Added edge types.</summary>
        public List<string> AddedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Removed edge types.</summary>
        public List<string> RemovedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Changed edge types.</summary>
        public List<string> ChangedEdgeTypes { get; set; } = new List<string>();

        /// <summary>Added rules.</summary>
        public List<string> AddedRules { get; set; } = new List<string>();

        /// <summary>Removed rules.</summary>
        public List<string> RemovedRules { get; set; } = new List<string>();

        /// <summary>Added concepts.</summary>
        public List<string> AddedConcepts { get; set; } = new List<string>();

        /// <summary>Removed concepts.</summary>
        public List<string> RemovedConcepts { get; set; } = new List<string>();

        /// <summary>Changed concepts.</summary>
        public List<string> ChangedConcepts { get; set; } = new List<string>();

        /// <summary>Whether the guidance changed.</summary>
        public bool GuidanceChanged { get; set; } = false;

        /// <summary>Whether the undeclared-type action changed.</summary>
        public bool UndeclaredTypeActionChanged { get; set; } = false;

        /// <summary>Whether the taxonomy changed.</summary>
        public bool TaxonomyChanged { get; set; } = false;
    }
}
