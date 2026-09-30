namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>Compares two ontology versions. Types are matched by normalized name, rules by what they say, concepts by key.</summary>
    public static class OntologyDiffer
    {
        #region Public-Methods

        /// <summary>Compare two versions.</summary>
        /// <param name="from">The older version, with contents loaded.</param>
        /// <param name="to">The newer version, with contents loaded.</param>
        /// <returns>The differences.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either version is null.</exception>
        public static OntologyVersionDiff Compare(OntologyVersion from, OntologyVersion to)
        {
            if (from == null) throw new ArgumentNullException(nameof(from));
            if (to == null) throw new ArgumentNullException(nameof(to));
            OntologyVersionDiff diff = new OntologyVersionDiff { FromVersionId = from.Id, ToVersionId = to.Id };

            CompareTypes(from.NodeTypes.ToDictionary(t => Ontology.NormalizeKey(t.Name), t => t), to.NodeTypes.ToDictionary(t => Ontology.NormalizeKey(t.Name), t => t),
                t => t.Name, t => t.Description, diff.AddedNodeTypes, diff.RemovedNodeTypes, diff.ChangedNodeTypes);
            CompareTypes(from.EdgeTypes.ToDictionary(t => Ontology.NormalizeKey(t.Name), t => t), to.EdgeTypes.ToDictionary(t => Ontology.NormalizeKey(t.Name), t => t),
                t => t.Name, t => t.Description, diff.AddedEdgeTypes, diff.RemovedEdgeTypes, diff.ChangedEdgeTypes);

            HashSet<string> fromRules = new HashSet<string>(from.Rules.Select(Describe), StringComparer.Ordinal);
            HashSet<string> toRules = new HashSet<string>(to.Rules.Select(Describe), StringComparer.Ordinal);
            diff.AddedRules = toRules.Where(r => !fromRules.Contains(r)).ToList();
            diff.RemovedRules = fromRules.Where(r => !toRules.Contains(r)).ToList();

            Dictionary<string, OntologyConcept> fromConcepts = new Dictionary<string, OntologyConcept>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in from.Concepts) fromConcepts[concept.Key] = concept;
            Dictionary<string, OntologyConcept> toConcepts = new Dictionary<string, OntologyConcept>(StringComparer.Ordinal);
            foreach (OntologyConcept concept in to.Concepts) toConcepts[concept.Key] = concept;
            foreach (KeyValuePair<string, OntologyConcept> pair in toConcepts)
            {
                OntologyConcept? old;
                if (!fromConcepts.TryGetValue(pair.Key, out old)) diff.AddedConcepts.Add(pair.Key);
                else if (Signature(old) != Signature(pair.Value)) diff.ChangedConcepts.Add(pair.Key);
            }
            foreach (string key in fromConcepts.Keys)
            {
                if (!toConcepts.ContainsKey(key)) diff.RemovedConcepts.Add(key);
            }

            diff.GuidanceChanged = !String.Equals((from.Guidance ?? String.Empty).Trim(), (to.Guidance ?? String.Empty).Trim(), StringComparison.Ordinal);
            diff.UndeclaredTypeActionChanged = from.UndeclaredTypeAction != to.UndeclaredTypeAction;
            return diff;
        }

        /// <summary>Describe a rule in words (also its identity for comparison).</summary>
        /// <param name="rule">The rule.</param>
        /// <returns>The description.</returns>
        public static string Describe(OntologyRule rule)
        {
            if (rule == null) throw new ArgumentNullException(nameof(rule));
            string action = " [" + rule.Action + "]";
            switch (rule.RuleType)
            {
                case OntologyRuleTypeEnum.EdgeEndpoints:
                    return rule.EdgeType + ": " + rule.FromNodeType + " -> " + rule.ToNodeType + action;
                case OntologyRuleTypeEnum.MaxOutgoing:
                    return rule.NodeType + " has at most " + rule.MaxCount.ToString(CultureInfo.InvariantCulture) + " " + rule.EdgeType + action;
                case OntologyRuleTypeEnum.RequiredField:
                    return rule.NodeType + " requires " + rule.Field + action;
                case OntologyRuleTypeEnum.NamePattern:
                    return rule.NodeType + " name matches " + rule.Pattern + action;
                case OntologyRuleTypeEnum.MinConfidence:
                    return (String.IsNullOrWhiteSpace(rule.NodeType) ? rule.EdgeType : rule.NodeType) + " confidence at least " +
                        rule.MinConfidence.ToString("0.##", CultureInfo.InvariantCulture) + action;
                default:
                    return rule.RuleType + action;
            }
        }

        #endregion

        #region Private-Methods

        private static void CompareTypes<T>(Dictionary<string, T> from, Dictionary<string, T> to, Func<T, string> name, Func<T, string?> description,
            List<string> added, List<string> removed, List<string> changed)
        {
            foreach (KeyValuePair<string, T> pair in to)
            {
                T? old;
                if (!from.TryGetValue(pair.Key, out old)) added.Add(name(pair.Value));
                else if (!String.Equals((description(old!) ?? String.Empty).Trim(), (description(pair.Value) ?? String.Empty).Trim(), StringComparison.Ordinal)) changed.Add(name(pair.Value));
            }
            foreach (KeyValuePair<string, T> pair in from)
            {
                if (!to.ContainsKey(pair.Key)) removed.Add(name(pair.Value));
            }
        }

        private static string Signature(OntologyConcept concept)
        {
            return concept.PrefLabel + "|" + String.Join(",", concept.AltLabels) + "|" + concept.BroaderKey + "|" + concept.NodeType + "|" +
                concept.Definition + "|" + concept.CaseSensitive.ToString(CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
