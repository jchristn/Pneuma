namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>An element that broke a rule of a subject's pinned ontology version.</summary>
    public class OntologyViolation
    {
        /// <summary>Violation identifier (prefix "ovl_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>Ingestion job, if found during ingestion.</summary>
        public string? JobId { get; set; } = null;

        /// <summary>Link, if found during ingestion.</summary>
        public string? LinkId { get; set; } = null;

        /// <summary>Validation operation, if found by one.</summary>
        public string? OperationId { get; set; } = null;

        /// <summary>Ontology version applied.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>Rule broken (null for an undeclared type).</summary>
        public string? RuleId { get; set; } = null;

        /// <summary>Rule type.</summary>
        public OntologyRuleTypeEnum? RuleType { get; set; } = null;

        /// <summary>Node or edge.</summary>
        public OntologyElementKindEnum ElementKind { get; set; } = OntologyElementKindEnum.Node;

        /// <summary>Node type.</summary>
        public string? NodeType { get; set; } = null;

        /// <summary>Node name.</summary>
        public string? NodeName { get; set; } = null;

        /// <summary>Edge type.</summary>
        public string? EdgeType { get; set; } = null;

        /// <summary>Source node type.</summary>
        public string? FromNodeType { get; set; } = null;

        /// <summary>Source node name.</summary>
        public string? FromNodeName { get; set; } = null;

        /// <summary>Target node type.</summary>
        public string? ToNodeType { get; set; } = null;

        /// <summary>Target node name.</summary>
        public string? ToNodeName { get; set; } = null;

        /// <summary>Node content (quarantined nodes).</summary>
        public string? Content { get; set; } = null;

        /// <summary>Model confidence.</summary>
        public double Confidence { get; set; } = 0.5;

        /// <summary>Action applied.</summary>
        public OntologyRuleActionEnum Action { get; set; } = OntologyRuleActionEnum.Warn;

        /// <summary>Review state.</summary>
        public OntologyViolationStatusEnum Status { get; set; } = OntologyViolationStatusEnum.Recorded;

        /// <summary>What was wrong.</summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>Reviewer.</summary>
        public string? ResolvedByUserId { get; set; } = null;

        /// <summary>UTC review timestamp.</summary>
        public DateTime? ResolvedUtc { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
