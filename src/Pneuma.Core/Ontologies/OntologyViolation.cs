namespace Pneuma.Core.Ontologies
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// One element of a subject's content that broke a rule of its pinned ontology version (or used a type the version
    /// does not declare). Found during ingestion (with a job and link) or by a validation operation (with an operation).
    /// A quarantined element carries enough of the candidate to be released into the graph later.
    /// </summary>
    public class OntologyViolation
    {
        #region Public-Members

        /// <summary>Violation identifier (prefix "ovl_").</summary>
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

        /// <summary>Subject identifier.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>Ingestion job that found it, or null when a validation operation found it.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Link whose content produced it, or null.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>Validation operation that found it, or null when ingestion found it.</summary>
        public string? OperationId { get; set; } = null;

        /// <summary>Ontology version whose rules were applied.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>The rule that was broken, or null for an undeclared type.</summary>
        public string? RuleId { get; set; } = null;

        /// <summary>The rule type, or null for an undeclared type.</summary>
        public OntologyRuleTypeEnum? RuleType { get; set; } = null;

        /// <summary>Whether the element is a node or an edge.</summary>
        public OntologyElementKindEnum ElementKind { get; set; } = OntologyElementKindEnum.Node;

        /// <summary>Node type (for a node).</summary>
        public string? NodeType { get; set; } = null;

        /// <summary>Node name (for a node).</summary>
        public string? NodeName { get; set; } = null;

        /// <summary>Edge type (for an edge).</summary>
        public string? EdgeType { get; set; } = null;

        /// <summary>Source node type (for an edge).</summary>
        public string? FromNodeType { get; set; } = null;

        /// <summary>Source node name (for an edge).</summary>
        public string? FromNodeName { get; set; } = null;

        /// <summary>Target node type (for an edge).</summary>
        public string? ToNodeType { get; set; } = null;

        /// <summary>Target node name (for an edge).</summary>
        public string? ToNodeName { get; set; } = null;

        /// <summary>Node content (for a quarantined node, so it can be released intact).</summary>
        public string? Content { get; set; } = null;

        /// <summary>Model confidence of the element. Clamped to [0, 1].</summary>
        public double Confidence
        {
            get { return _Confidence; }
            set { _Confidence = Math.Clamp(value, 0.0, 1.0); }
        }

        /// <summary>The action that was applied.</summary>
        public OntologyRuleActionEnum Action { get; set; } = OntologyRuleActionEnum.Warn;

        /// <summary>Review state.</summary>
        public OntologyViolationStatusEnum Status { get; set; } = OntologyViolationStatusEnum.Recorded;

        /// <summary>What was wrong, in words.</summary>
        public string Message { get; set; } = String.Empty;

        /// <summary>User who released or dismissed it, if reviewed.</summary>
        public string? ResolvedByUserId { get; set; } = null;

        /// <summary>UTC review timestamp, if reviewed.</summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateOntologyViolationId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private double _Confidence = 0.5;

        #endregion
    }
}
