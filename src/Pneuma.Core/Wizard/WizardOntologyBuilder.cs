namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ontologies;

    /// <summary>
    /// Converts between the wizard's ontology draft and an ontology version, and cleans up a draft: type names in the
    /// house style, no duplicates, relationship endpoints that name declared node types, question numbers in range, and
    /// the Subject node type always present. A relationship may appear more than once with different endpoints; it
    /// becomes one relationship type with an endpoint rule per pair.
    /// </summary>
    public static class WizardOntologyBuilder
    {
        #region Public-Methods

        /// <summary>The built-in Default template as a wizard draft (the starting point shown to the model).</summary>
        /// <returns>The draft.</returns>
        public static WizardOntology FromTemplate()
        {
            OntologyVersion template = OntologyTemplates.Build(OntologyTemplates.Default) ?? new OntologyVersion();
            WizardOntology draft = new WizardOntology { Guidance = template.Guidance };
            foreach (OntologyNodeType node in template.NodeTypes)
                draft.NodeTypes.Add(new WizardNodeType { Name = node.Name, Description = node.Description });
            foreach (OntologyEdgeType edge in template.EdgeTypes)
            {
                List<OntologyRule> endpoints = template.Rules
                    .Where(r => r.RuleType == OntologyRuleTypeEnum.EdgeEndpoints && String.Equals(r.EdgeType, edge.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (endpoints.Count == 0) draft.EdgeTypes.Add(new WizardEdgeType { Name = edge.Name, Description = edge.Description });
                foreach (OntologyRule rule in endpoints)
                    draft.EdgeTypes.Add(new WizardEdgeType { Name = edge.Name, Description = edge.Description, From = rule.FromNodeType, To = rule.ToNodeType });
            }
            return draft;
        }

        /// <summary>
        /// Clean up a draft in place: normalize names, drop empty and duplicate types, resolve endpoints to declared node
        /// types, keep question numbers within range, cap the number of types, and make sure a Subject node type exists.
        /// </summary>
        /// <param name="draft">The draft.</param>
        /// <param name="questionCount">How many questions the draft has (question numbers are 1-based).</param>
        /// <param name="maxTypes">Most node plus relationship types kept.</param>
        /// <param name="warnings">Receives what was changed or dropped.</param>
        /// <returns>The same draft.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft"/> or <paramref name="warnings"/> is null.</exception>
        public static WizardOntology Normalize(WizardOntology draft, int questionCount, int maxTypes, List<string> warnings)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));

            List<WizardNodeType> nodes = new List<WizardNodeType>();
            HashSet<string> nodeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WizardNodeType node in draft.NodeTypes ?? new List<WizardNodeType>())
            {
                if (node == null) continue;
                string name = ToPascal(node.Name);
                if (name.Length == 0) continue;
                if (!nodeNames.Add(name))
                {
                    warnings.Add("Dropped a duplicate node type " + name + ".");
                    continue;
                }
                node.Name = name;
                node.Description = Trim(node.Description, 500);
                node.Questions = CleanQuestions(node.Questions, questionCount);
                nodes.Add(node);
            }
            if (!nodeNames.Contains(Ontology.NodeSubject))
            {
                nodes.Insert(0, new WizardNodeType { Name = Ontology.NodeSubject, Description = "The subject the archive is about." });
                nodeNames.Add(Ontology.NodeSubject);
            }

            List<WizardEdgeType> edges = new List<WizardEdgeType>();
            HashSet<string> edgeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (WizardEdgeType edge in draft.EdgeTypes ?? new List<WizardEdgeType>())
            {
                if (edge == null || String.IsNullOrWhiteSpace(edge.Name)) continue;
                string name = Ontology.ToUpperSnake(edge.Name.Trim());
                if (name.Length == 0) continue;
                edge.Name = Trim(name, 128)!;
                edge.Description = Trim(edge.Description, 500);
                edge.From = ResolveNode(edge.From, nodes, edge.Name, warnings);
                edge.To = ResolveNode(edge.To, nodes, edge.Name, warnings);
                string key = edge.Name + "|" + (edge.From ?? "*") + "|" + (edge.To ?? "*");
                if (!edgeKeys.Add(key)) continue;
                edge.Questions = CleanQuestions(edge.Questions, questionCount);
                edges.Add(edge);
            }

            int limit = Math.Max(2, maxTypes);
            if (nodes.Count + edges.Count > limit)
            {
                int nodeCap = Math.Max(1, Math.Min(nodes.Count, limit / 2));
                int edgeCap = Math.Max(0, limit - nodeCap);
                if (nodes.Count > nodeCap || edges.Count > edgeCap) warnings.Add("Kept the first " + limit + " types; the ontology had more.");
                nodes = nodes.Take(nodeCap).ToList();
                HashSet<string> kept = new HashSet<string>(nodes.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
                edges = edges.Where(e => (e.From == null || kept.Contains(e.From)) && (e.To == null || kept.Contains(e.To))).Take(edgeCap).ToList();
            }

            draft.NodeTypes = nodes;
            draft.EdgeTypes = edges;
            draft.Guidance = Trim(draft.Guidance, 2000);
            return draft;
        }

        /// <summary>Build an unsaved ontology version (no tenant, ontology, or number) from a normalized draft.</summary>
        /// <param name="draft">The draft.</param>
        /// <returns>The version contents.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="draft"/> is null.</exception>
        public static OntologyVersion ToVersion(WizardOntology draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            OntologyVersion version = new OntologyVersion { Guidance = draft.Guidance, UndeclaredTypeAction = UndeclaredTypeActionEnum.Allow };
            foreach (WizardNodeType node in draft.NodeTypes)
                version.NodeTypes.Add(new OntologyNodeType { Name = node.Name, Description = node.Description });
            foreach (WizardEdgeType edge in draft.EdgeTypes)
            {
                if (!version.EdgeTypes.Any(e => String.Equals(e.Name, edge.Name, StringComparison.OrdinalIgnoreCase)))
                    version.EdgeTypes.Add(new OntologyEdgeType { Name = edge.Name, Description = edge.Description });
                if (edge.From != null && edge.To != null)
                {
                    version.Rules.Add(new OntologyRule
                    {
                        RuleType = OntologyRuleTypeEnum.EdgeEndpoints,
                        EdgeType = edge.Name,
                        FromNodeType = edge.From,
                        ToNodeType = edge.To,
                        Action = OntologyRuleActionEnum.Warn
                    });
                }
            }
            return version;
        }

        /// <summary>The ontology as the classifier sees it (the same rendering a pinned version uses).</summary>
        /// <param name="draft">The draft.</param>
        /// <returns>The rendered definition.</returns>
        public static string Render(WizardOntology draft)
        {
            return OntologyDefinitionRenderer.Render(ToVersion(draft));
        }

        /// <summary>Normalize a node type name to PascalCase (letters and digits only).</summary>
        /// <param name="value">The name.</param>
        /// <returns>The normalized name, or "" when nothing usable remains.</returns>
        public static string ToPascal(string? value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            StringBuilder sb = new StringBuilder();
            bool upperNext = true;
            foreach (char c in value.Trim())
            {
                if (Char.IsLetterOrDigit(c))
                {
                    sb.Append(upperNext ? Char.ToUpperInvariant(c) : c);
                    upperNext = false;
                }
                else upperNext = true;
            }
            string result = sb.ToString();
            return result.Length > 128 ? result.Substring(0, 128) : result;
        }

        #endregion

        #region Private-Methods

        private static string? ResolveNode(string? name, List<WizardNodeType> nodes, string edgeName, List<string> warnings)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Trim() == "*") return null;
            string pascal = ToPascal(name);
            WizardNodeType? match = nodes.FirstOrDefault(n => String.Equals(n.Name, pascal, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.Name;
            warnings.Add(edgeName + " referred to an unknown node type " + name.Trim() + "; that end now accepts any type.");
            return null;
        }

        private static List<int> CleanQuestions(List<int>? questions, int questionCount)
        {
            if (questions == null) return new List<int>();
            return questions.Where(q => q >= 1 && q <= questionCount).Distinct().OrderBy(q => q).ToList();
        }

        private static string? Trim(string? value, int length)
        {
            if (String.IsNullOrWhiteSpace(value)) return null;
            string trimmed = value.Trim();
            return trimmed.Length <= length ? trimmed : trimmed.Substring(0, length);
        }

        #endregion
    }
}
