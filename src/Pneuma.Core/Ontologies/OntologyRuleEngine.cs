namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Applies an ontology version's rules. During ingestion it enforces them on each document's candidate subgraph
    /// (keeping, reversing, dropping, or quarantining elements); for a validation operation it checks the stored graph
    /// and reports what it finds without changing anything. Each offending element produces one violation whose action is
    /// the strongest of the rules it broke.
    /// </summary>
    public class OntologyRuleEngine
    {
        #region Private-Members

        private static readonly HashSet<string> _StructuralNodeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            Ontology.NodeSource, Ontology.NodeCell, Ontology.NodeChunk, Ontology.NodeCommunitySummary
        };

        private static readonly HashSet<string> _StructuralEdgeTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            Ontology.EdgeDerivedFromSource, Ontology.EdgeHasCell, Ontology.EdgeHasChunk, Ontology.EdgeBroader
        };

        private readonly OntologyVersion _Version;
        private readonly OntologyTypeResolver _Types;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the engine for a version.</summary>
        /// <param name="version">The ontology version, with its contents loaded.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public OntologyRuleEngine(OntologyVersion version)
        {
            _Version = version ?? throw new ArgumentNullException(nameof(version));
            _Types = new OntologyTypeResolver(version);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Enforce the rules on a candidate subgraph in place: dropped and quarantined elements are removed, edges that a
        /// Reverse rule can fix are reversed, and edges whose endpoint was removed are removed with it.
        /// </summary>
        /// <param name="subgraph">The candidate subgraph (types already canonicalized).</param>
        /// <returns>What was done.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> is null.</exception>
        public OntologyRuleOutcome ApplyToCandidate(CandidateSubgraph subgraph)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));
            OntologyRuleOutcome outcome = new OntologyRuleOutcome();
            Dictionary<string, CandidateNode> byRef = new Dictionary<string, CandidateNode>(StringComparer.Ordinal);
            HashSet<string> removedRefs = new HashSet<string>(StringComparer.Ordinal);

            List<CandidateNode> keptNodes = new List<CandidateNode>();
            foreach (CandidateNode node in subgraph.Nodes)
            {
                if (!String.IsNullOrEmpty(node.Ref) && !byRef.ContainsKey(node.Ref)) byRef[node.Ref] = node;
                OntologyViolation? violation = CheckNode(node.NodeType, node.Name, node.CanonicalName, node.Content, node.Rights, node.Authority, node.Confidence);
                if (violation == null)
                {
                    keptNodes.Add(node);
                    continue;
                }
                violation.Content = node.Content;
                Record(outcome, violation);
                if (Removes(violation.Action))
                {
                    if (!String.IsNullOrEmpty(node.Ref)) removedRefs.Add(node.Ref);
                }
                else
                {
                    keptNodes.Add(node);
                }
            }
            subgraph.Nodes = keptNodes;

            List<CandidateEdge> keptEdges = new List<CandidateEdge>();
            foreach (CandidateEdge edge in subgraph.Edges)
            {
                if (removedRefs.Contains(edge.FromRef) || removedRefs.Contains(edge.ToRef))
                {
                    outcome.DroppedWithNode++;
                    continue;
                }
                CandidateNode? from;
                CandidateNode? to;
                if (!byRef.TryGetValue(edge.FromRef, out from) || !byRef.TryGetValue(edge.ToRef, out to))
                {
                    keptEdges.Add(edge);
                    continue;
                }
                bool reverse;
                OntologyViolation? violation = CheckEdge(edge.EdgeType, from.NodeType, from.Name, to.NodeType, to.Name, edge.Confidence, true, out reverse);
                if (reverse)
                {
                    string swap = edge.FromRef;
                    edge.FromRef = edge.ToRef;
                    edge.ToRef = swap;
                    outcome.Reversed++;
                }
                if (violation != null)
                {
                    Record(outcome, violation);
                    if (Removes(violation.Action)) continue;
                }
                keptEdges.Add(edge);
            }
            subgraph.Edges = keptEdges;

            ApplyMaxOutgoing(subgraph, byRef, outcome);
            return outcome;
        }

        /// <summary>Check a stored graph against the rules without changing it.</summary>
        /// <param name="nodes">The subject's nodes.</param>
        /// <param name="edges">The edges among them.</param>
        /// <returns>One violation per offending element (tenant and subject not set).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="nodes"/> or <paramref name="edges"/> is null.</exception>
        public List<OntologyViolation> ValidateGraph(List<GraphNode> nodes, List<GraphEdge> edges)
        {
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            List<OntologyViolation> violations = new List<OntologyViolation>();
            Dictionary<string, GraphNode> byId = new Dictionary<string, GraphNode>(StringComparer.Ordinal);

            foreach (GraphNode node in nodes)
            {
                if (!String.IsNullOrEmpty(node.Id)) byId[node.Id] = node;
                if (IsStructural(node)) continue;
                OntologyViolation? violation = CheckNode(node.NodeType, node.Name, node.CanonicalName, node.Content, Tag(node.Tags, Ontology.TagRights), Tag(node.Tags, Ontology.TagAuthority), Confidence(node.Tags));
                if (violation != null) violations.Add(violation);
            }

            Dictionary<string, List<GraphEdge>> outgoing = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
            foreach (GraphEdge edge in edges)
            {
                if (IsStructural(edge)) continue;
                GraphNode? from;
                GraphNode? to;
                if (!byId.TryGetValue(edge.FromNodeId, out from) || !byId.TryGetValue(edge.ToNodeId, out to)) continue;
                bool reverse;
                OntologyViolation? violation = CheckEdge(edge.EdgeType, from.NodeType, from.Name, to.NodeType, to.Name, Confidence(edge.Tags), false, out reverse);
                if (violation != null) violations.Add(violation);
                List<GraphEdge>? list;
                if (!outgoing.TryGetValue(edge.FromNodeId, out list))
                {
                    list = new List<GraphEdge>();
                    outgoing[edge.FromNodeId] = list;
                }
                list.Add(edge);
            }

            foreach (OntologyRule rule in _Version.Rules)
            {
                if (rule.RuleType != OntologyRuleTypeEnum.MaxOutgoing) continue;
                foreach (KeyValuePair<string, List<GraphEdge>> pair in outgoing)
                {
                    GraphNode node = byId[pair.Key];
                    if (!OntologyTypeResolver.Same(rule.NodeType, node.NodeType)) continue;
                    List<GraphEdge> matching = pair.Value.Where(e => OntologyTypeResolver.Same(rule.EdgeType, e.EdgeType)).ToList();
                    if (matching.Count <= rule.MaxCount) continue;
                    OntologyViolation violation = NewViolation(rule.Id, rule.RuleType, OntologyElementKindEnum.Node, rule.Action,
                        node.NodeType + " '" + node.Name + "' has " + matching.Count.ToString(CultureInfo.InvariantCulture) + " " + rule.EdgeType +
                        " relationships; at most " + rule.MaxCount.ToString(CultureInfo.InvariantCulture) + " are allowed.");
                    violation.NodeType = node.NodeType;
                    violation.NodeName = node.Name;
                    violation.EdgeType = rule.EdgeType;
                    violations.Add(violation);
                }
            }
            return violations;
        }

        #endregion

        #region Private-Methods

        private OntologyViolation? CheckNode(string type, string name, string? canonicalName, string? content, string? rights, string? authority, double confidence)
        {
            List<string> messages = new List<string>();
            OntologyRule? strongest = null;
            OntologyRuleActionEnum? undeclared = null;

            if (!_Types.AcceptsNode(type))
            {
                undeclared = OntologyRuleChecks.ForUndeclared(_Version.UndeclaredTypeAction);
                if (undeclared != null) messages.Add("Node type '" + type + "' is not declared by the ontology.");
            }
            foreach (OntologyRule rule in _Version.Rules)
            {
                string? message = OntologyRuleChecks.CheckNode(rule, type, name, canonicalName, content, rights, authority, confidence);
                if (message == null) continue;
                messages.Add(message);
                if (strongest == null || OntologyRuleChecks.Severity(rule.Action) > OntologyRuleChecks.Severity(strongest.Action)) strongest = rule;
            }
            if (messages.Count == 0) return null;

            OntologyViolation violation = Combine(strongest, undeclared, OntologyElementKindEnum.Node, messages);
            violation.NodeType = type;
            violation.NodeName = name;
            violation.Confidence = confidence;
            return violation;
        }

        private OntologyViolation? CheckEdge(string edgeType, string fromType, string fromName, string toType, string toName, double confidence, bool canReverse, out bool reverse)
        {
            reverse = false;
            List<string> messages = new List<string>();
            OntologyRule? strongest = null;
            OntologyRuleActionEnum? undeclared = null;

            if (!_Types.AcceptsEdge(edgeType))
            {
                undeclared = OntologyRuleChecks.ForUndeclared(_Version.UndeclaredTypeAction);
                if (undeclared != null) messages.Add("Relationship type '" + edgeType + "' is not declared by the ontology.");
            }

            List<OntologyRule> endpoints = OntologyRuleChecks.EndpointRules(_Version.Rules, edgeType);
            if (endpoints.Count > 0 && !OntologyRuleChecks.Allows(endpoints, fromType, toType))
            {
                bool reversible = OntologyRuleChecks.Allows(endpoints, toType, fromType) && endpoints.Any(r => r.Action == OntologyRuleActionEnum.Reverse);
                if (reversible && canReverse)
                {
                    reverse = true;
                    OntologyRule reverser = endpoints.First(r => r.Action == OntologyRuleActionEnum.Reverse);
                    OntologyViolation reversed = NewViolation(reverser.Id, reverser.RuleType, OntologyElementKindEnum.Edge, OntologyRuleActionEnum.Reverse,
                        edgeType + " from " + fromType + " to " + toType + " was reversed; allowed: " + OntologyRuleChecks.Directions(endpoints) + ".");
                    FillEdge(reversed, edgeType, toType, toName, fromType, fromName, confidence);
                    return reversed;
                }
                OntologyRule rule = OntologyRuleChecks.Strongest(endpoints);
                messages.Add(edgeType + " from " + fromType + " to " + toType + " is not allowed; allowed: " + OntologyRuleChecks.Directions(endpoints) + ".");
                strongest = rule;
            }
            foreach (OntologyRule rule in _Version.Rules)
            {
                string? message = OntologyRuleChecks.CheckEdgeConfidence(rule, edgeType, confidence);
                if (message == null) continue;
                messages.Add(message);
                if (strongest == null || OntologyRuleChecks.Severity(rule.Action) > OntologyRuleChecks.Severity(strongest.Action)) strongest = rule;
            }
            if (messages.Count == 0) return null;

            OntologyViolation violation = Combine(strongest, undeclared, OntologyElementKindEnum.Edge, messages);
            FillEdge(violation, edgeType, fromType, fromName, toType, toName, confidence);
            return violation;
        }

        private void ApplyMaxOutgoing(CandidateSubgraph subgraph, Dictionary<string, CandidateNode> byRef, OntologyRuleOutcome outcome)
        {
            foreach (OntologyRule rule in _Version.Rules)
            {
                if (rule.RuleType != OntologyRuleTypeEnum.MaxOutgoing) continue;
                Dictionary<string, List<CandidateEdge>> bySource = new Dictionary<string, List<CandidateEdge>>(StringComparer.Ordinal);
                foreach (CandidateEdge edge in subgraph.Edges)
                {
                    CandidateNode? from;
                    if (!OntologyTypeResolver.Same(rule.EdgeType, edge.EdgeType) || !byRef.TryGetValue(edge.FromRef, out from)) continue;
                    if (!OntologyTypeResolver.Same(rule.NodeType, from.NodeType)) continue;
                    List<CandidateEdge>? list;
                    if (!bySource.TryGetValue(edge.FromRef, out list))
                    {
                        list = new List<CandidateEdge>();
                        bySource[edge.FromRef] = list;
                    }
                    list.Add(edge);
                }

                HashSet<CandidateEdge> removed = new HashSet<CandidateEdge>();
                foreach (KeyValuePair<string, List<CandidateEdge>> pair in bySource)
                {
                    if (pair.Value.Count <= rule.MaxCount) continue;
                    CandidateNode from = byRef[pair.Key];
                    foreach (CandidateEdge extra in pair.Value.OrderByDescending(e => e.Confidence).Skip(rule.MaxCount))
                    {
                        CandidateNode? to;
                        byRef.TryGetValue(extra.ToRef, out to);
                        OntologyViolation violation = NewViolation(rule.Id, rule.RuleType, OntologyElementKindEnum.Edge, rule.Action,
                            from.NodeType + " '" + from.Name + "' has " + pair.Value.Count.ToString(CultureInfo.InvariantCulture) + " " + rule.EdgeType +
                            " relationships; at most " + rule.MaxCount.ToString(CultureInfo.InvariantCulture) + " are allowed.");
                        FillEdge(violation, extra.EdgeType, from.NodeType, from.Name, to?.NodeType, to?.Name, extra.Confidence);
                        Record(outcome, violation);
                        if (Removes(rule.Action)) removed.Add(extra);
                    }
                }
                if (removed.Count > 0) subgraph.Edges = subgraph.Edges.Where(e => !removed.Contains(e)).ToList();
            }
        }

        private OntologyViolation Combine(OntologyRule? strongest, OntologyRuleActionEnum? undeclared, OntologyElementKindEnum kind, List<string> messages)
        {
            OntologyRuleActionEnum action = strongest?.Action ?? OntologyRuleActionEnum.Warn;
            if (action == OntologyRuleActionEnum.Reverse) action = OntologyRuleActionEnum.Drop;
            bool undeclaredWins = undeclared != null && (strongest == null || OntologyRuleChecks.Severity(undeclared.Value) > OntologyRuleChecks.Severity(action));
            if (undeclaredWins) return NewViolation(null, null, kind, undeclared!.Value, String.Join(" ", messages));
            return NewViolation(strongest?.Id, strongest?.RuleType, kind, action, String.Join(" ", messages));
        }

        private OntologyViolation NewViolation(string? ruleId, OntologyRuleTypeEnum? ruleType, OntologyElementKindEnum kind, OntologyRuleActionEnum action, string message)
        {
            return new OntologyViolation
            {
                OntologyVersionId = _Version.Id,
                RuleId = ruleId,
                RuleType = ruleType,
                ElementKind = kind,
                Action = action,
                Status = action == OntologyRuleActionEnum.Quarantine ? OntologyViolationStatusEnum.Quarantined : OntologyViolationStatusEnum.Recorded,
                Message = message
            };
        }

        private static void FillEdge(OntologyViolation violation, string edgeType, string? fromType, string? fromName, string? toType, string? toName, double confidence)
        {
            violation.EdgeType = edgeType;
            violation.FromNodeType = fromType;
            violation.FromNodeName = fromName;
            violation.ToNodeType = toType;
            violation.ToNodeName = toName;
            violation.Confidence = confidence;
        }

        private static void Record(OntologyRuleOutcome outcome, OntologyViolation violation)
        {
            outcome.Violations.Add(violation);
            switch (violation.Action)
            {
                case OntologyRuleActionEnum.Quarantine: outcome.Quarantined++; break;
                case OntologyRuleActionEnum.Drop: outcome.Dropped++; break;
                case OntologyRuleActionEnum.Reverse: break;
                default: outcome.Warned++; break;
            }
        }

        private static bool Removes(OntologyRuleActionEnum action)
        {
            return action == OntologyRuleActionEnum.Drop || action == OntologyRuleActionEnum.Quarantine;
        }

        private static bool IsStructural(GraphNode node)
        {
            return _StructuralNodeTypes.Contains(node.NodeType) || String.Equals(Tag(node.Tags, Ontology.TagAssertedBy), Ontology.AssertedByTaxonomy, StringComparison.Ordinal);
        }

        private static bool IsStructural(GraphEdge edge)
        {
            return _StructuralEdgeTypes.Contains(edge.EdgeType) || String.Equals(Tag(edge.Tags, Ontology.TagAssertedBy), Ontology.AssertedByTaxonomy, StringComparison.Ordinal);
        }

        private static string? Tag(Dictionary<string, string>? tags, string key)
        {
            string? value;
            return tags != null && tags.TryGetValue(key, out value) ? value : null;
        }

        private static double Confidence(Dictionary<string, string>? tags)
        {
            string? raw = Tag(tags, Ontology.TagConfidence);
            double value;
            return raw != null && Double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 1.0;
        }

        #endregion
    }
}
