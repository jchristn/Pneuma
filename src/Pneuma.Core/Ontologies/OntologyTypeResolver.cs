namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Maps model-emitted node and edge types onto the types an ontology version declares. Names are compared without
    /// case, spaces, or punctuation, so "person", "PERSON", and "Per-son" all resolve to a declared "Person".
    /// </summary>
    public class OntologyTypeResolver
    {
        #region Public-Members

        /// <summary>Whether the version declares any node types (when it declares none, every node type is accepted).</summary>
        public bool DeclaresNodeTypes { get { return _Nodes.Count > 0; } }

        /// <summary>Whether the version declares any edge types (when it declares none, every edge type is accepted).</summary>
        public bool DeclaresEdgeTypes { get { return _Edges.Count > 0; } }

        #endregion

        #region Private-Members

        private readonly Dictionary<string, string> _Nodes = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _Edges = new Dictionary<string, string>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>Build a resolver for a version.</summary>
        /// <param name="version">The ontology version.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="version"/> is null.</exception>
        public OntologyTypeResolver(OntologyVersion version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            foreach (OntologyNodeType type in version.NodeTypes) _Nodes[Ontology.NormalizeKey(type.Name)] = type.Name;
            foreach (OntologyEdgeType type in version.EdgeTypes) _Edges[Ontology.NormalizeKey(type.Name)] = type.Name;
        }

        #endregion

        #region Public-Methods

        /// <summary>The declared node type an emitted type names, or null.</summary>
        /// <param name="raw">The emitted type.</param>
        /// <returns>The declared name, or null when the version does not declare it.</returns>
        public string? DeclaredNode(string? raw)
        {
            string? found;
            return _Nodes.TryGetValue(Ontology.NormalizeKey(raw), out found) ? found : null;
        }

        /// <summary>The declared edge type an emitted type names, or null.</summary>
        /// <param name="raw">The emitted type.</param>
        /// <returns>The declared name, or null when the version does not declare it.</returns>
        public string? DeclaredEdge(string? raw)
        {
            string? found;
            return _Edges.TryGetValue(Ontology.NormalizeKey(raw), out found) ? found : null;
        }

        /// <summary>Canonicalize an emitted node type: the declared name when declared, otherwise the built-in canonical form.</summary>
        /// <param name="raw">The emitted type.</param>
        /// <returns>The canonical node type.</returns>
        public string CanonicalNode(string? raw)
        {
            string? declared = DeclaredNode(raw);
            if (declared != null) return declared;
            string builtIn = Ontology.CanonicalNodeType(raw);
            return DeclaredNode(builtIn) ?? builtIn;
        }

        /// <summary>Canonicalize an emitted edge type: the declared name when declared, otherwise the built-in canonical form.</summary>
        /// <param name="raw">The emitted type.</param>
        /// <returns>The canonical edge type.</returns>
        public string CanonicalEdge(string? raw)
        {
            string? declared = DeclaredEdge(raw);
            if (declared != null) return declared;
            string builtIn = Ontology.CanonicalEdgeType(raw);
            return DeclaredEdge(builtIn) ?? builtIn;
        }

        /// <summary>Whether a node type is declared, or the version declares no node types at all.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when accepted.</returns>
        public bool AcceptsNode(string? type)
        {
            return !DeclaresNodeTypes || DeclaredNode(type) != null;
        }

        /// <summary>Whether an edge type is declared, or the version declares no edge types at all.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True when accepted.</returns>
        public bool AcceptsEdge(string? type)
        {
            return !DeclaresEdgeTypes || DeclaredEdge(type) != null;
        }

        /// <summary>Whether two type names are the same (compared without case, spaces, or punctuation).</summary>
        /// <param name="a">First name.</param>
        /// <param name="b">Second name.</param>
        /// <returns>True when they name the same type.</returns>
        public static bool Same(string? a, string? b)
        {
            string ka = Ontology.NormalizeKey(a);
            return ka.Length > 0 && String.Equals(ka, Ontology.NormalizeKey(b), StringComparison.Ordinal);
        }

        #endregion
    }
}
