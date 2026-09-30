namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// One numbered version of a tenant ontology. A draft is editable; an approved version is immutable and can be
    /// pinned by subjects; a retired version is immutable and cannot be newly pinned. The child lists (node types, edge
    /// types, rules, concepts) are loaded when a single version is read and are empty in version listings.
    /// </summary>
    public class OntologyVersion
    {
        #region Public-Members

        /// <summary>Version identifier (prefix "onv_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Owning ontology identifier.</summary>
        public string OntologyId
        {
            get { return _OntologyId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(OntologyId)); _OntologyId = value; }
        }

        /// <summary>Version number, unique within the ontology and starting at 1. Minimum 1.</summary>
        public int VersionNumber
        {
            get { return _VersionNumber; }
            set { _VersionNumber = value < 1 ? 1 : value; }
        }

        /// <summary>Lifecycle state.</summary>
        public OntologyVersionStatusEnum Status { get; set; } = OntologyVersionStatusEnum.Draft;

        /// <summary>
        /// Optional extraction guidance in natural language, appended after the types and rules in the definition the
        /// classifier sees (for example "Prefer Organization over Topic for named teams").
        /// </summary>
        public string? Guidance { get; set; } = null;

        /// <summary>What happens to a node or edge whose type this version does not declare. Default Allow.</summary>
        public UndeclaredTypeActionEnum UndeclaredTypeAction { get; set; } = UndeclaredTypeActionEnum.Allow;

        /// <summary>Optional summary of what changed in this version, set by the author or at approval.</summary>
        public string? ChangeSummary { get; set; } = null;

        /// <summary>The version this draft was copied from, if any.</summary>
        public string? BasedOnVersionId { get; set; } = null;

        /// <summary>User who created the version, if known.</summary>
        public string? CreatedByUserId { get; set; } = null;

        /// <summary>User who approved the version, if approved.</summary>
        public string? ApprovedByUserId { get; set; } = null;

        /// <summary>UTC approval timestamp, if approved.</summary>
        public DateTime? ApprovedUtc { get; set; } = null;

        /// <summary>UTC retirement timestamp, if retired.</summary>
        public DateTime? RetiredUtc { get; set; } = null;

        /// <summary>Node types, in display order. Never null.</summary>
        public List<OntologyNodeType> NodeTypes
        {
            get { return _NodeTypes; }
            set { _NodeTypes = value ?? new List<OntologyNodeType>(); }
        }

        /// <summary>Edge (relationship) types, in display order. Never null.</summary>
        public List<OntologyEdgeType> EdgeTypes
        {
            get { return _EdgeTypes; }
            set { _EdgeTypes = value ?? new List<OntologyEdgeType>(); }
        }

        /// <summary>Constraint rules, in evaluation order. Never null.</summary>
        public List<OntologyRule> Rules
        {
            get { return _Rules; }
            set { _Rules = value ?? new List<OntologyRule>(); }
        }

        /// <summary>Taxonomy concepts, in display order. Never null.</summary>
        public List<OntologyConcept> Concepts
        {
            get { return _Concepts; }
            set { _Concepts = value ?? new List<OntologyConcept>(); }
        }

        /// <summary>Number of node types (set in listings, where the child lists are not loaded).</summary>
        public int NodeTypeCount { get; set; } = 0;

        /// <summary>Number of edge types (set in listings).</summary>
        public int EdgeTypeCount { get; set; } = 0;

        /// <summary>Number of rules (set in listings).</summary>
        public int RuleCount { get; set; } = 0;

        /// <summary>Number of taxonomy concepts (set in listings).</summary>
        public int ConceptCount { get; set; } = 0;

        /// <summary>
        /// Problems that block approval (a rule naming an undeclared type, a pattern that does not compile, and so on).
        /// Computed when a single version is read or saved; not stored. Empty for listings and approved versions.
        /// </summary>
        public List<string> Problems
        {
            get { return _Problems; }
            set { _Problems = value ?? new List<string>(); }
        }

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateOntologyVersionId();
        private string _TenantId = String.Empty;
        private string _OntologyId = String.Empty;
        private int _VersionNumber = 1;
        private List<OntologyNodeType> _NodeTypes = new List<OntologyNodeType>();
        private List<OntologyEdgeType> _EdgeTypes = new List<OntologyEdgeType>();
        private List<OntologyRule> _Rules = new List<OntologyRule>();
        private List<OntologyConcept> _Concepts = new List<OntologyConcept>();
        private List<string> _Problems = new List<string>();

        #endregion
    }
}
