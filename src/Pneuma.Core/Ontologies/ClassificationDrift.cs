namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// The difference between two classifications of the same input. Nodes are compared by canonical type and name, edges
    /// by type and the canonical names of their endpoints, all without case, so only substantive changes count.
    /// </summary>
    public class ClassificationDrift
    {
        #region Public-Members

        /// <summary>Whether the two results differ.</summary>
        public bool Changed { get { return OnlyInFirst.Count > 0 || OnlyInSecond.Count > 0; } }

        /// <summary>Elements only the first result had.</summary>
        public List<string> OnlyInFirst { get; set; } = new List<string>();

        /// <summary>Elements only the second result had.</summary>
        public List<string> OnlyInSecond { get; set; } = new List<string>();

        /// <summary>Elements both results had.</summary>
        public int Shared { get; set; } = 0;

        #endregion

        #region Public-Methods

        /// <summary>Compare two classifications.</summary>
        /// <param name="first">The first result.</param>
        /// <param name="second">The second result.</param>
        /// <returns>The difference.</returns>
        /// <exception cref="ArgumentNullException">Thrown when either result is null.</exception>
        public static ClassificationDrift Compare(CandidateSubgraph first, CandidateSubgraph second)
        {
            if (first == null) throw new ArgumentNullException(nameof(first));
            if (second == null) throw new ArgumentNullException(nameof(second));
            HashSet<string> a = Signatures(first);
            HashSet<string> b = Signatures(second);
            return new ClassificationDrift
            {
                OnlyInFirst = a.Where(s => !b.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                OnlyInSecond = b.Where(s => !a.Contains(s)).OrderBy(s => s, StringComparer.Ordinal).ToList(),
                Shared = a.Count(s => b.Contains(s))
            };
        }

        /// <summary>Describe the difference in words.</summary>
        /// <returns>The description.</returns>
        public string Describe()
        {
            if (!Changed) return "Both runs gave the same " + Shared.ToString(CultureInfo.InvariantCulture) + " element(s).";
            List<string> parts = new List<string> { Shared.ToString(CultureInfo.InvariantCulture) + " element(s) in both runs" };
            if (OnlyInFirst.Count > 0) parts.Add("only in the first: " + String.Join("; ", OnlyInFirst.Take(10)));
            if (OnlyInSecond.Count > 0) parts.Add("only in the second: " + String.Join("; ", OnlyInSecond.Take(10)));
            return String.Join(". ", parts) + ".";
        }

        #endregion

        #region Private-Methods

        private static HashSet<string> Signatures(CandidateSubgraph subgraph)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> nameByRef = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (CandidateNode node in subgraph.Nodes ?? new List<CandidateNode>())
            {
                if (String.IsNullOrWhiteSpace(node.Name)) continue;
                string type = Ontology.CanonicalNodeType(node.NodeType);
                string name = (String.IsNullOrWhiteSpace(node.CanonicalName) ? node.Name : node.CanonicalName!).Trim().ToLowerInvariant();
                if (!String.IsNullOrEmpty(node.Ref)) nameByRef[node.Ref] = name;
                result.Add(type + " '" + name + "'");
            }
            foreach (CandidateEdge edge in subgraph.Edges ?? new List<CandidateEdge>())
            {
                string? from;
                string? to;
                if (!nameByRef.TryGetValue(edge.FromRef, out from) || !nameByRef.TryGetValue(edge.ToRef, out to)) continue;
                result.Add("'" + from + "' " + Ontology.CanonicalEdgeType(edge.EdgeType) + " '" + to + "'");
            }
            return result;
        }

        #endregion
    }
}
