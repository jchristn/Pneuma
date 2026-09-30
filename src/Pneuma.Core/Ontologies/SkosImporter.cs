namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Pneuma.Core.Enums;
    using VDS.RDF;

    /// <summary>
    /// Reads SKOS concepts from Turtle or JSON-LD. Each <c>skos:Concept</c> becomes a concept keyed by its <c>skos:notation</c> (or its IRI when it has none), with its
    /// preferred label (English or untagged preferred), alternative and hidden labels, definition, and broader concept.
    /// Concepts without a preferred label are skipped with a warning.
    /// </summary>
    public static class SkosImporter
    {
        #region Public-Members

        /// <summary>The largest document accepted, in characters.</summary>
        public const int MaxDocumentLength = 20 * 1024 * 1024;

        #endregion

        #region Public-Methods

        /// <summary>Parse a SKOS document.</summary>
        /// <param name="text">The document.</param>
        /// <param name="format">Turtle or JSON-LD.</param>
        /// <param name="warnings">Receives what was skipped.</param>
        /// <returns>The concepts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> or <paramref name="warnings"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the document is not valid RDF.</exception>
        public static List<OntologyConcept> Parse(string text, RdfFormatEnum format, List<string> warnings)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (warnings == null) throw new ArgumentNullException(nameof(warnings));
            IGraph graph;
            try
            {
                graph = RdfWriting.Parse(text, format == RdfFormatEnum.JsonLd);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                throw new ArgumentException("The document is not valid " + (format == RdfFormatEnum.JsonLd ? "JSON-LD" : "Turtle") + ": " + e.Message, e);
            }

            INode type = graph.CreateUriNode(new Uri(RdfWriting.Rdf + "type"));
            INode conceptClass = graph.CreateUriNode(new Uri(RdfWriting.Skos + "Concept"));
            INode prefLabel = graph.CreateUriNode(new Uri(RdfWriting.Skos + "prefLabel"));
            INode altLabel = graph.CreateUriNode(new Uri(RdfWriting.Skos + "altLabel"));
            INode hiddenLabel = graph.CreateUriNode(new Uri(RdfWriting.Skos + "hiddenLabel"));
            INode definition = graph.CreateUriNode(new Uri(RdfWriting.Skos + "definition"));
            INode broader = graph.CreateUriNode(new Uri(RdfWriting.Skos + "broader"));

            INode notation = graph.CreateUriNode(new Uri(RdfWriting.Skos + "notation"));
            List<INode> subjects = graph.GetTriplesWithPredicateObject(type, conceptClass).Select(t => t.Subject).Distinct().ToList();

            // A concept's key is its skos:notation when it has one (so an exported taxonomy re-imports with the same keys),
            // otherwise its IRI; broader references are translated through the same map.
            Dictionary<INode, string> keys = new Dictionary<INode, string>();
            foreach (INode subject in subjects)
            {
                string? code = BestLiteral(graph.GetTriplesWithSubjectPredicate(subject, notation).Select(t => t.Object));
                keys[subject] = String.IsNullOrWhiteSpace(code) ? Key(subject) : code!;
            }

            List<OntologyConcept> concepts = new List<OntologyConcept>();
            foreach (INode subject in subjects)
            {
                string key = keys[subject];
                string? label = BestLiteral(graph.GetTriplesWithSubjectPredicate(subject, prefLabel).Select(t => t.Object));
                if (String.IsNullOrWhiteSpace(label))
                {
                    warnings.Add("Skipped " + key + ": it has no preferred label.");
                    continue;
                }
                List<string> alts = graph.GetTriplesWithSubjectPredicate(subject, altLabel).Concat(graph.GetTriplesWithSubjectPredicate(subject, hiddenLabel))
                    .Select(t => t.Object).OfType<ILiteralNode>().Select(l => l.Value.Trim())
                    .Where(v => v.Length > 0 && v.Length <= 256 && !String.Equals(v, label, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal).Take(OntologyVersionValidator.MaxAltLabels).ToList();
                INode? parent = graph.GetTriplesWithSubjectPredicate(subject, broader).Select(t => t.Object).FirstOrDefault();
                concepts.Add(new OntologyConcept
                {
                    Key = key,
                    PrefLabel = label!.Length > 256 ? label.Substring(0, 256) : label,
                    AltLabels = alts,
                    Definition = BestLiteral(graph.GetTriplesWithSubjectPredicate(subject, definition).Select(t => t.Object)),
                    BroaderKey = parent == null ? null : (keys.TryGetValue(parent, out string? parentKey) ? parentKey : Key(parent))
                });
            }
            return concepts;
        }

        #endregion

        #region Private-Methods

        private static string Key(INode node)
        {
            if (node is IUriNode uri) return uri.Uri.AbsoluteUri;
            if (node is IBlankNode blank) return "_:" + blank.InternalID;
            return node.ToString();
        }

        private static string? BestLiteral(IEnumerable<INode> nodes)
        {
            List<ILiteralNode> literals = nodes.OfType<ILiteralNode>().ToList();
            if (literals.Count == 0) return null;
            ILiteralNode? preferred = literals.FirstOrDefault(l => String.IsNullOrEmpty(l.Language))
                ?? literals.FirstOrDefault(l => l.Language.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                ?? literals[0];
            return preferred.Value.Trim();
        }

        #endregion
    }
}
