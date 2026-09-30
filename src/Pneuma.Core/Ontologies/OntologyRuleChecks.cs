namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.RegularExpressions;
    using Pneuma.Core.Enums;

    /// <summary>
    /// The individual rule checks shared by candidate-subgraph enforcement and stored-graph validation. Each check
    /// returns a message when the element breaks the rule, or null when it does not.
    /// </summary>
    internal static class OntologyRuleChecks
    {
        #region Internal-Methods

        /// <summary>Severity of an action for picking the strongest one (Reverse counts as Drop when it cannot reverse).</summary>
        internal static int Severity(OntologyRuleActionEnum action)
        {
            switch (action)
            {
                case OntologyRuleActionEnum.Quarantine: return 3;
                case OntologyRuleActionEnum.Drop: return 2;
                case OntologyRuleActionEnum.Reverse: return 2;
                default: return 1;
            }
        }

        /// <summary>The rule action an undeclared-type action maps to, or null for Allow.</summary>
        internal static OntologyRuleActionEnum? ForUndeclared(UndeclaredTypeActionEnum action)
        {
            switch (action)
            {
                case UndeclaredTypeActionEnum.Warn: return OntologyRuleActionEnum.Warn;
                case UndeclaredTypeActionEnum.Drop: return OntologyRuleActionEnum.Drop;
                case UndeclaredTypeActionEnum.Quarantine: return OntologyRuleActionEnum.Quarantine;
                default: return null;
            }
        }

        /// <summary>Check a node rule against a node's fields.</summary>
        internal static string? CheckNode(OntologyRule rule, string type, string name, string? canonicalName, string? content, string? rights, string? authority, double confidence)
        {
            if (!OntologyTypeResolver.Same(rule.NodeType, type)) return null;
            switch (rule.RuleType)
            {
                case OntologyRuleTypeEnum.RequiredField:
                    string? value = FieldValue(rule.Field, canonicalName, content, rights, authority);
                    if (!String.IsNullOrWhiteSpace(value)) return null;
                    return type + " '" + name + "' has no " + FieldName(rule.Field) + ".";
                case OntologyRuleTypeEnum.NamePattern:
                    if (String.IsNullOrWhiteSpace(rule.Pattern) || Matches(rule.Pattern!, name)) return null;
                    return type + " '" + name + "' does not match the name pattern " + rule.Pattern + ".";
                case OntologyRuleTypeEnum.MinConfidence:
                    if (!String.IsNullOrWhiteSpace(rule.EdgeType) || confidence >= rule.MinConfidence) return null;
                    return type + " '" + name + "' has confidence " + confidence.ToString("0.##", CultureInfo.InvariantCulture) + ", below " + rule.MinConfidence.ToString("0.##", CultureInfo.InvariantCulture) + ".";
                default:
                    return null;
            }
        }

        /// <summary>Check an edge's confidence against a MinConfidence rule for its type.</summary>
        internal static string? CheckEdgeConfidence(OntologyRule rule, string edgeType, double confidence)
        {
            if (rule.RuleType != OntologyRuleTypeEnum.MinConfidence || !OntologyTypeResolver.Same(rule.EdgeType, edgeType)) return null;
            if (confidence >= rule.MinConfidence) return null;
            return edgeType + " has confidence " + confidence.ToString("0.##", CultureInfo.InvariantCulture) + ", below " + rule.MinConfidence.ToString("0.##", CultureInfo.InvariantCulture) + ".";
        }

        /// <summary>The endpoint rules for an edge type.</summary>
        internal static List<OntologyRule> EndpointRules(List<OntologyRule> rules, string edgeType)
        {
            List<OntologyRule> result = new List<OntologyRule>();
            foreach (OntologyRule rule in rules)
            {
                if (rule.RuleType == OntologyRuleTypeEnum.EdgeEndpoints && OntologyTypeResolver.Same(rule.EdgeType, edgeType)) result.Add(rule);
            }
            return result;
        }

        /// <summary>Whether any endpoint rule allows the direction.</summary>
        internal static bool Allows(List<OntologyRule> endpointRules, string fromType, string toType)
        {
            foreach (OntologyRule rule in endpointRules)
            {
                if (OntologyTypeResolver.Same(rule.FromNodeType, fromType) && OntologyTypeResolver.Same(rule.ToNodeType, toType)) return true;
            }
            return false;
        }

        /// <summary>The strongest action among endpoint rules, treating Reverse as Drop.</summary>
        internal static OntologyRule Strongest(List<OntologyRule> rules)
        {
            OntologyRule strongest = rules[0];
            foreach (OntologyRule rule in rules)
            {
                if (Severity(rule.Action) > Severity(strongest.Action)) strongest = rule;
            }
            return strongest;
        }

        /// <summary>A short list of allowed directions for messages.</summary>
        internal static string Directions(List<OntologyRule> endpointRules)
        {
            List<string> parts = new List<string>();
            foreach (OntologyRule rule in endpointRules) parts.Add(rule.FromNodeType + " -> " + rule.ToNodeType);
            return String.Join(", ", parts);
        }

        #endregion

        #region Private-Methods

        private static string? FieldValue(OntologyNodeFieldEnum? field, string? canonicalName, string? content, string? rights, string? authority)
        {
            switch (field)
            {
                case OntologyNodeFieldEnum.Content: return content;
                case OntologyNodeFieldEnum.Rights: return rights;
                case OntologyNodeFieldEnum.Authority: return authority;
                case OntologyNodeFieldEnum.CanonicalName: return canonicalName;
                default: return null;
            }
        }

        private static string FieldName(OntologyNodeFieldEnum? field)
        {
            switch (field)
            {
                case OntologyNodeFieldEnum.Content: return "content";
                case OntologyNodeFieldEnum.Rights: return "rights classification";
                case OntologyNodeFieldEnum.Authority: return "authority classification";
                case OntologyNodeFieldEnum.CanonicalName: return "canonical name";
                default: return "value";
            }
        }

        private static bool Matches(string pattern, string value)
        {
            try
            {
                return Regex.IsMatch(value ?? String.Empty, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(250));
            }
            catch (ArgumentException)
            {
                // An invalid pattern cannot be approved; treat a stored one as not applicable rather than failing ingestion.
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                return true;
            }
        }

        #endregion
    }
}
