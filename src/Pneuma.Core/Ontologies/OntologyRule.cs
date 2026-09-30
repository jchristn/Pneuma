namespace Pneuma.Core.Ontologies
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A constraint rule in an ontology version. Which fields apply depends on <see cref="RuleType"/>:
    /// <list type="bullet">
    /// <item><description>EdgeEndpoints: <see cref="EdgeType"/>, <see cref="FromNodeType"/>, <see cref="ToNodeType"/>.</description></item>
    /// <item><description>MaxOutgoing: <see cref="NodeType"/>, <see cref="EdgeType"/>, <see cref="MaxCount"/>.</description></item>
    /// <item><description>RequiredField: <see cref="NodeType"/>, <see cref="Field"/>.</description></item>
    /// <item><description>NamePattern: <see cref="NodeType"/>, <see cref="Pattern"/> (a .NET regular expression).</description></item>
    /// <item><description>MinConfidence: <see cref="NodeType"/> or <see cref="EdgeType"/>, <see cref="MinConfidence"/>.</description></item>
    /// </list>
    /// </summary>
    public class OntologyRule
    {
        #region Public-Members

        /// <summary>Rule identifier (prefix "orl_"). Kept when a draft is saved with the same id, so violations stay linked.</summary>
        public string Id
        {
            get { return _Id; }
            set { _Id = String.IsNullOrWhiteSpace(value) ? IdGenerator.GenerateOntologyRuleId() : value; }
        }

        /// <summary>What the rule checks.</summary>
        public OntologyRuleTypeEnum RuleType { get; set; } = OntologyRuleTypeEnum.EdgeEndpoints;

        /// <summary>The node type the rule applies to (MaxOutgoing, RequiredField, NamePattern, and node MinConfidence rules).</summary>
        public string? NodeType { get; set; } = null;

        /// <summary>The edge type the rule applies to (EdgeEndpoints, MaxOutgoing, and edge MinConfidence rules).</summary>
        public string? EdgeType { get; set; } = null;

        /// <summary>The allowed source node type (EdgeEndpoints).</summary>
        public string? FromNodeType { get; set; } = null;

        /// <summary>The allowed target node type (EdgeEndpoints).</summary>
        public string? ToNodeType { get; set; } = null;

        /// <summary>The field that must have a value (RequiredField).</summary>
        public OntologyNodeFieldEnum? Field { get; set; } = null;

        /// <summary>The regular expression a node's name must match (NamePattern). At most 512 characters.</summary>
        public string? Pattern { get; set; } = null;

        /// <summary>The largest number of outgoing edges allowed (MaxOutgoing). Clamped to [0, 100000].</summary>
        public int MaxCount
        {
            get { return _MaxCount; }
            set { _MaxCount = Math.Clamp(value, 0, 100000); }
        }

        /// <summary>The smallest model confidence allowed (MinConfidence). Clamped to [0, 1].</summary>
        public double MinConfidence
        {
            get { return _MinConfidence; }
            set { _MinConfidence = Math.Clamp(value, 0.0, 1.0); }
        }

        /// <summary>What happens to an element that breaks the rule. Default Warn.</summary>
        public OntologyRuleActionEnum Action { get; set; } = OntologyRuleActionEnum.Warn;

        /// <summary>Optional explanation shown with violations.</summary>
        public string? Description { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateOntologyRuleId();
        private int _MaxCount = 1;
        private double _MinConfidence = 0.5;

        #endregion
    }
}
