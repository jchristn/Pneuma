namespace Pneuma.Sdk.Models
{
    using System;
    using Pneuma.Sdk.Enums;

    /// <summary>A constraint rule of an ontology version.</summary>
    public class OntologyRule
    {
        /// <summary>Rule identifier (prefix "orl_"); keep it when saving a draft so violations stay linked.</summary>
        public string? Id { get; set; } = null;

        /// <summary>What the rule checks.</summary>
        public OntologyRuleTypeEnum RuleType { get; set; } = OntologyRuleTypeEnum.EdgeEndpoints;

        /// <summary>Node type.</summary>
        public string? NodeType { get; set; } = null;

        /// <summary>Edge type.</summary>
        public string? EdgeType { get; set; } = null;

        /// <summary>Allowed source node type (EdgeEndpoints).</summary>
        public string? FromNodeType { get; set; } = null;

        /// <summary>Allowed target node type (EdgeEndpoints).</summary>
        public string? ToNodeType { get; set; } = null;

        /// <summary>Field that must have a value (RequiredField).</summary>
        public OntologyNodeFieldEnum? Field { get; set; } = null;

        /// <summary>Regular expression (NamePattern).</summary>
        public string? Pattern { get; set; } = null;

        /// <summary>Largest number of outgoing edges (MaxOutgoing).</summary>
        public int MaxCount { get; set; } = 1;

        /// <summary>Smallest confidence (MinConfidence).</summary>
        public double MinConfidence { get; set; } = 0.5;

        /// <summary>What happens to an element that breaks the rule.</summary>
        public OntologyRuleActionEnum Action { get; set; } = OntologyRuleActionEnum.Warn;

        /// <summary>Explanation.</summary>
        public string? Description { get; set; } = null;
    }
}
