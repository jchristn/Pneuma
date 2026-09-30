namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Checks an ontology version's contents. Errors make the contents unsavable (missing or duplicate names, lengths,
    /// counts); problems are semantic issues that block approval but not saving a draft (a rule that names an undeclared
    /// type, a pattern that does not compile, a broader concept that does not exist).
    /// </summary>
    public static class OntologyVersionValidator
    {
        #region Public-Members

        /// <summary>The most node types a version can declare.</summary>
        public const int MaxNodeTypes = 500;

        /// <summary>The most edge types a version can declare.</summary>
        public const int MaxEdgeTypes = 500;

        /// <summary>The most rules a version can have.</summary>
        public const int MaxRules = 1000;

        /// <summary>The most taxonomy concepts a version can have.</summary>
        public const int MaxConcepts = 20000;

        /// <summary>The most alternative labels a concept can have.</summary>
        public const int MaxAltLabels = 50;

        /// <summary>The longest guidance text, in characters.</summary>
        public const int MaxGuidanceLength = 20000;

        #endregion

        #region Public-Methods

        /// <summary>Errors that prevent saving the contents.</summary>
        /// <param name="version">The version.</param>
        /// <returns>The errors (empty when savable).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public static List<string> Errors(OntologyVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            List<string> errors = new List<string>();
            if (version.NodeTypes.Count > MaxNodeTypes) errors.Add("A version can declare at most " + MaxNodeTypes + " node types.");
            if (version.EdgeTypes.Count > MaxEdgeTypes) errors.Add("A version can declare at most " + MaxEdgeTypes + " edge types.");
            if (version.Rules.Count > MaxRules) errors.Add("A version can have at most " + MaxRules + " rules.");
            if (version.Concepts.Count > MaxConcepts) errors.Add("A version can have at most " + MaxConcepts + " taxonomy concepts.");
            if (version.Guidance != null && version.Guidance.Length > MaxGuidanceLength) errors.Add("Guidance can be at most " + MaxGuidanceLength + " characters.");

            CheckTypeNames(errors, "node type", NodeNames(version));
            CheckTypeNames(errors, "edge type", EdgeNames(version));

            HashSet<string> ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (OntologyRule rule in version.Rules)
            {
                if (!ruleIds.Add(rule.Id)) errors.Add("Rule id " + rule.Id + " is used more than once.");
                if (rule.Pattern != null && rule.Pattern.Length > 512) errors.Add("A rule pattern can be at most 512 characters.");
            }

            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in version.Concepts)
            {
                if (String.IsNullOrWhiteSpace(concept.PrefLabel)) errors.Add("Every concept needs a preferred label.");
                if (concept.Key.Length > 512) errors.Add("Concept key '" + Short(concept.Key) + "' is longer than 512 characters.");
                if (concept.PrefLabel.Length > 256) errors.Add("Concept label '" + Short(concept.PrefLabel) + "' is longer than 256 characters.");
                if (!keys.Add(concept.Key)) errors.Add("Concept key '" + Short(concept.Key) + "' is used more than once.");
                if (concept.AltLabels.Count > MaxAltLabels) errors.Add("Concept '" + Short(concept.PrefLabel) + "' has more than " + MaxAltLabels + " alternative labels.");
                foreach (string label in concept.AltLabels)
                {
                    if (String.IsNullOrWhiteSpace(label)) errors.Add("Concept '" + Short(concept.PrefLabel) + "' has an empty alternative label.");
                    else if (label.Length > 256) errors.Add("An alternative label of '" + Short(concept.PrefLabel) + "' is longer than 256 characters.");
                }
            }
            return errors;
        }

        /// <summary>Problems that prevent approving the contents (it must also have no errors).</summary>
        /// <param name="version">The version.</param>
        /// <returns>The problems (empty when approvable).</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public static List<string> Problems(OntologyVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            List<string> problems = new List<string>();
            if (version.NodeTypes.Count == 0) problems.Add("Declare at least one node type.");

            HashSet<string> nodeKeys = KeySet(NodeNames(version));
            HashSet<string> edgeKeys = KeySet(EdgeNames(version));
            for (int i = 0; i < version.Rules.Count; i++)
            {
                OntologyRule rule = version.Rules[i];
                string where = "Rule " + (i + 1).ToString(CultureInfo.InvariantCulture) + " (" + rule.RuleType + ")";
                CheckRule(problems, where, rule, nodeKeys, edgeKeys);
            }
            CheckConcepts(problems, version, nodeKeys);
            return problems;
        }

        #endregion

        #region Private-Methods

        private static void CheckRule(List<string> problems, string where, OntologyRule rule, HashSet<string> nodeKeys, HashSet<string> edgeKeys)
        {
            if (rule.Action == OntologyRuleActionEnum.Reverse && rule.RuleType != OntologyRuleTypeEnum.EdgeEndpoints)
                problems.Add(where + ": the Reverse action applies only to edge endpoint rules.");

            switch (rule.RuleType)
            {
                case OntologyRuleTypeEnum.EdgeEndpoints:
                    RequireEdge(problems, where, rule.EdgeType, edgeKeys);
                    RequireNode(problems, where, "source node type", rule.FromNodeType, nodeKeys);
                    RequireNode(problems, where, "target node type", rule.ToNodeType, nodeKeys);
                    break;
                case OntologyRuleTypeEnum.MaxOutgoing:
                    RequireNode(problems, where, "node type", rule.NodeType, nodeKeys);
                    RequireEdge(problems, where, rule.EdgeType, edgeKeys);
                    break;
                case OntologyRuleTypeEnum.RequiredField:
                    RequireNode(problems, where, "node type", rule.NodeType, nodeKeys);
                    if (rule.Field == null) problems.Add(where + ": choose the field that must have a value.");
                    break;
                case OntologyRuleTypeEnum.NamePattern:
                    RequireNode(problems, where, "node type", rule.NodeType, nodeKeys);
                    if (String.IsNullOrWhiteSpace(rule.Pattern)) problems.Add(where + ": a pattern is required.");
                    else if (!Compiles(rule.Pattern!)) problems.Add(where + ": the pattern is not a valid regular expression.");
                    break;
                case OntologyRuleTypeEnum.MinConfidence:
                    bool hasNode = !String.IsNullOrWhiteSpace(rule.NodeType);
                    bool hasEdge = !String.IsNullOrWhiteSpace(rule.EdgeType);
                    if (hasNode == hasEdge) problems.Add(where + ": name either a node type or an edge type.");
                    else if (hasNode) RequireNode(problems, where, "node type", rule.NodeType, nodeKeys);
                    else RequireEdge(problems, where, rule.EdgeType, edgeKeys);
                    break;
                default:
                    problems.Add(where + ": unknown rule type.");
                    break;
            }
        }

        private static void CheckConcepts(List<string> problems, OntologyVersion version, HashSet<string> nodeKeys)
        {
            Dictionary<string, OntologyConcept> byKey = new Dictionary<string, OntologyConcept>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in version.Concepts) byKey[concept.Key] = concept;

            Dictionary<string, string> labelOwner = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in version.Concepts)
            {
                if (nodeKeys.Count > 0 && !nodeKeys.Contains(Ontology.NormalizeKey(concept.NodeType)))
                    problems.Add("Concept '" + Short(concept.PrefLabel) + "' uses node type '" + concept.NodeType + "', which this version does not declare.");
                if (!String.IsNullOrWhiteSpace(concept.BroaderKey) && !byKey.ContainsKey(concept.BroaderKey!))
                    problems.Add("Concept '" + Short(concept.PrefLabel) + "' has a broader concept '" + Short(concept.BroaderKey!) + "' that does not exist.");

                List<string> labels = new List<string> { concept.PrefLabel };
                labels.AddRange(concept.AltLabels);
                foreach (string label in labels)
                {
                    string normalized = TaxonomyMatcher.NormalizeLabel(label);
                    if (normalized.Length == 0) continue;
                    string? owner;
                    if (labelOwner.TryGetValue(normalized, out owner))
                    {
                        if (!String.Equals(owner, concept.Key, StringComparison.Ordinal))
                            problems.Add("Label '" + Short(label) + "' belongs to more than one concept ('" + Short(owner) + "' and '" + Short(concept.Key) + "').");
                    }
                    else
                    {
                        labelOwner[normalized] = concept.Key;
                    }
                }
            }

            // A broader chain must end: follow each concept's parents and report a cycle once.
            HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in version.Concepts)
            {
                HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal) { concept.Key };
                string? current = concept.BroaderKey;
                while (!String.IsNullOrWhiteSpace(current) && byKey.ContainsKey(current!))
                {
                    if (!seen.Add(current!))
                    {
                        if (reported.Add(current!)) problems.Add("The broader concepts of '" + Short(concept.PrefLabel) + "' form a cycle.");
                        break;
                    }
                    current = byKey[current!].BroaderKey;
                }
            }
        }

        private static void RequireNode(List<string> problems, string where, string what, string? name, HashSet<string> nodeKeys)
        {
            if (String.IsNullOrWhiteSpace(name)) problems.Add(where + ": a " + what + " is required.");
            else if (!nodeKeys.Contains(Ontology.NormalizeKey(name))) problems.Add(where + ": node type '" + name + "' is not declared.");
        }

        private static void RequireEdge(List<string> problems, string where, string? name, HashSet<string> edgeKeys)
        {
            if (String.IsNullOrWhiteSpace(name)) problems.Add(where + ": an edge type is required.");
            else if (!edgeKeys.Contains(Ontology.NormalizeKey(name))) problems.Add(where + ": edge type '" + name + "' is not declared.");
        }

        private static void CheckTypeNames(List<string> errors, string what, List<string> names)
        {
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (String.IsNullOrWhiteSpace(name))
                {
                    errors.Add("Every " + what + " needs a name.");
                    continue;
                }
                if (name.Length > 128) errors.Add("The " + what + " '" + Short(name) + "' is longer than 128 characters.");
                string key = Ontology.NormalizeKey(name);
                if (key.Length == 0) errors.Add("The " + what + " '" + Short(name) + "' needs at least one letter or digit.");
                else if (!seen.Add(key)) errors.Add("The " + what + " '" + Short(name) + "' is declared more than once (names are compared without case, spaces, or punctuation).");
            }
        }

        private static List<string> NodeNames(OntologyVersion version)
        {
            List<string> names = new List<string>();
            foreach (OntologyNodeType type in version.NodeTypes) names.Add(type.Name);
            return names;
        }

        private static List<string> EdgeNames(OntologyVersion version)
        {
            List<string> names = new List<string>();
            foreach (OntologyEdgeType type in version.EdgeTypes) names.Add(type.Name);
            return names;
        }

        private static HashSet<string> KeySet(List<string> names)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names) keys.Add(Ontology.NormalizeKey(name));
            return keys;
        }

        private static bool Compiles(string pattern)
        {
            try
            {
                Regex regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
                return regex != null;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static string Short(string value)
        {
            return value.Length <= 60 ? value : value.Substring(0, 60) + "...";
        }

        #endregion
    }
}
