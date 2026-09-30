namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>A numbered version of an ontology.</summary>
    public class OntologyVersion
    {
        /// <summary>Version identifier (prefix "onv_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Owning ontology identifier.</summary>
        public string OntologyId { get; set; } = string.Empty;

        /// <summary>Version number.</summary>
        public int VersionNumber { get; set; } = 1;

        /// <summary>Lifecycle state.</summary>
        public OntologyVersionStatusEnum Status { get; set; } = OntologyVersionStatusEnum.Draft;

        /// <summary>Extraction guidance.</summary>
        public string? Guidance { get; set; } = null;

        /// <summary>What happens to undeclared types.</summary>
        public UndeclaredTypeActionEnum UndeclaredTypeAction { get; set; } = UndeclaredTypeActionEnum.Allow;

        /// <summary>Change summary.</summary>
        public string? ChangeSummary { get; set; } = null;

        /// <summary>The version this was copied from.</summary>
        public string? BasedOnVersionId { get; set; } = null;

        /// <summary>Creating user.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>Approving user.</summary>
        public string? ApprovedByUserId { get; set; } = null;

        /// <summary>UTC approval timestamp.</summary>
        public DateTime? ApprovedUtc { get; set; } = null;

        /// <summary>UTC retirement timestamp.</summary>
        public DateTime? RetiredUtc { get; set; } = null;

        /// <summary>Node types.</summary>
        public List<OntologyNodeType> NodeTypes { get; set; } = new List<OntologyNodeType>();

        /// <summary>Edge types.</summary>
        public List<OntologyEdgeType> EdgeTypes { get; set; } = new List<OntologyEdgeType>();

        /// <summary>Rules.</summary>
        public List<OntologyRule> Rules { get; set; } = new List<OntologyRule>();

        /// <summary>Taxonomy concepts.</summary>
        public List<OntologyConcept> Concepts { get; set; } = new List<OntologyConcept>();

        /// <summary>Node type count (listings).</summary>
        public int NodeTypeCount { get; set; } = 0;

        /// <summary>Edge type count (listings).</summary>
        public int EdgeTypeCount { get; set; } = 0;

        /// <summary>Rule count (listings).</summary>
        public int RuleCount { get; set; } = 0;

        /// <summary>Concept count (listings).</summary>
        public int ConceptCount { get; set; } = 0;

        /// <summary>Problems that block approval.</summary>
        public List<string> Problems { get; set; } = new List<string>();

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
