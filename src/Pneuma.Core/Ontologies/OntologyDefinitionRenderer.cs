namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Renders an ontology version into the natural-language definition the classifier sees: node types, relationship
    /// types with their allowed endpoints, the other rules, and the version's guidance. The rendered text is shown to
    /// operators, so what the model is told is never hidden. The taxonomy is not rendered (it can be thousands of
    /// concepts); matched concepts are given per document through the <c>taxonomy.hint</c> prompt instead.
    /// </summary>
    public static class OntologyDefinitionRenderer
    {
        #region Public-Methods

        /// <summary>Render a version.</summary>
        /// <param name="version">The version, with its contents loaded.</param>
        /// <returns>The definition text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public static string Render(OntologyVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("Node types:");
            foreach (OntologyNodeType type in version.NodeTypes) sb.AppendLine("- " + type.Name + Describe(type.Description));

            if (version.EdgeTypes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Relationship types (edges), written FROM -> TO:");
                foreach (OntologyEdgeType type in version.EdgeTypes)
                {
                    List<string> endpoints = new List<string>();
                    foreach (OntologyRule rule in version.Rules)
                    {
                        if (rule.RuleType != OntologyRuleTypeEnum.EdgeEndpoints || !OntologyTypeResolver.Same(rule.EdgeType, type.Name)) continue;
                        endpoints.Add(rule.FromNodeType + " -> " + rule.ToNodeType);
                    }
                    string shape = endpoints.Count == 0 ? String.Empty : " (" + String.Join(", ", endpoints) + ")";
                    sb.AppendLine("- " + type.Name + shape + Describe(type.Description));
                }
            }

            List<string> constraints = new List<string>();
            foreach (OntologyRule rule in version.Rules)
            {
                string? line = Constraint(rule);
                if (line != null) constraints.Add(line);
            }
            if (constraints.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Constraints:");
                foreach (string line in constraints) sb.AppendLine("- " + line);
            }

            if (version.UndeclaredTypeAction != UndeclaredTypeActionEnum.Allow)
            {
                sb.AppendLine();
                sb.AppendLine("Use only the node and relationship types listed above; anything else is discarded or held for review.");
            }

            if (!String.IsNullOrWhiteSpace(version.Guidance))
            {
                sb.AppendLine();
                sb.AppendLine(version.Guidance!.Trim());
            }
            return sb.ToString().TrimEnd();
        }

        #endregion

        #region Private-Methods

        private static string Describe(string? description)
        {
            return String.IsNullOrWhiteSpace(description) ? String.Empty : ": " + description!.Trim();
        }

        private static string? Constraint(OntologyRule rule)
        {
            switch (rule.RuleType)
            {
                case OntologyRuleTypeEnum.MaxOutgoing:
                    return "A " + rule.NodeType + " has at most " + rule.MaxCount.ToString(CultureInfo.InvariantCulture) + " " + rule.EdgeType + " relationship" + (rule.MaxCount == 1 ? "." : "s.");
                case OntologyRuleTypeEnum.RequiredField:
                    return "Every " + rule.NodeType + " must have " + FieldWords(rule.Field) + ".";
                case OntologyRuleTypeEnum.NamePattern:
                    return "A " + rule.NodeType + " name must match the pattern " + rule.Pattern + ".";
                case OntologyRuleTypeEnum.MinConfidence:
                    string what = String.IsNullOrWhiteSpace(rule.NodeType) ? rule.EdgeType + " relationship" : rule.NodeType;
                    return "Only assert a " + what + " with confidence of at least " + rule.MinConfidence.ToString("0.##", CultureInfo.InvariantCulture) + ".";
                default:
                    return null;
            }
        }

        private static string FieldWords(OntologyNodeFieldEnum? field)
        {
            switch (field)
            {
                case OntologyNodeFieldEnum.Content: return "content";
                case OntologyNodeFieldEnum.Rights: return "a rights classification";
                case OntologyNodeFieldEnum.Authority: return "an authority classification";
                case OntologyNodeFieldEnum.CanonicalName: return "a canonical name";
                default: return "a value";
            }
        }

        #endregion
    }
}
