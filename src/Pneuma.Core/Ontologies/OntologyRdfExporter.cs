namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Pneuma.Core.Enums;
    using VDS.RDF;

    /// <summary>
    /// Writes an ontology version as OWL and SKOS: node types become <c>owl:Class</c>, edge types <c>owl:ObjectProperty</c>
    /// (with <c>rdfs:domain</c> and <c>rdfs:range</c> when exactly one direction is allowed), and the taxonomy a
    /// <c>skos:ConceptScheme</c> of <c>skos:Concept</c> resources. A concept whose key is an absolute IRI keeps that IRI.
    /// </summary>
    public static class OntologyRdfExporter
    {
        #region Public-Methods

        /// <summary>Serialize a version.</summary>
        /// <param name="ontology">The ontology.</param>
        /// <param name="version">The version, with contents loaded.</param>
        /// <param name="format">Turtle or JSON-LD.</param>
        /// <param name="baseIri">Base IRI, or null for <see cref="GraphExporter.DefaultBaseIri"/>.</param>
        /// <returns>The serialized ontology.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="ontology"/> or <paramref name="version"/> is null.</exception>
        public static string Serialize(TenantOntology ontology, OntologyVersion version, RdfFormatEnum format, string? baseIri)
        {
            if (ontology == null) throw new ArgumentNullException(nameof(ontology));
            if (version == null) throw new ArgumentNullException(nameof(version));
            Graph g = Build(ontology, version, baseIri);
            return format == RdfFormatEnum.JsonLd ? RdfWriting.JsonLd(g) : RdfWriting.Turtle(g);
        }

        #endregion

        #region Private-Methods

        private static Graph Build(TenantOntology ontology, OntologyVersion version, string? baseIri)
        {
            string root = (String.IsNullOrWhiteSpace(baseIri) ? GraphExporter.DefaultBaseIri : baseIri!) + "ontology:" + ontology.Id + ":v" + version.VersionNumber.ToString(CultureInfo.InvariantCulture);
            string types = GraphExporterTypes(baseIri);
            Graph g = new Graph();
            g.NamespaceMap.AddNamespace("owl", new Uri(RdfWriting.Owl));
            g.NamespaceMap.AddNamespace("rdfs", new Uri(RdfWriting.Rdfs));
            g.NamespaceMap.AddNamespace("skos", new Uri(RdfWriting.Skos));

            INode a = g.CreateUriNode(new Uri(RdfWriting.Rdf + "type"));
            INode label = g.CreateUriNode(new Uri(RdfWriting.Rdfs + "label"));
            INode comment = g.CreateUriNode(new Uri(RdfWriting.Rdfs + "comment"));
            INode ontologyNode = g.CreateUriNode(new Uri(root));
            g.Assert(ontologyNode, a, g.CreateUriNode(new Uri(RdfWriting.Owl + "Ontology")));
            g.Assert(ontologyNode, label, g.CreateLiteralNode(ontology.Name));
            g.Assert(ontologyNode, g.CreateUriNode(new Uri(RdfWriting.Owl + "versionInfo")), g.CreateLiteralNode(version.VersionNumber.ToString(CultureInfo.InvariantCulture)));
            if (!String.IsNullOrWhiteSpace(ontology.Description)) g.Assert(ontologyNode, comment, g.CreateLiteralNode(ontology.Description!));

            foreach (OntologyNodeType type in version.NodeTypes)
            {
                INode cls = g.CreateUriNode(new Uri(types + "type:" + Uri.EscapeDataString(type.Name)));
                g.Assert(cls, a, g.CreateUriNode(new Uri(RdfWriting.Owl + "Class")));
                g.Assert(cls, label, g.CreateLiteralNode(type.Name));
                if (!String.IsNullOrWhiteSpace(type.Description)) g.Assert(cls, comment, g.CreateLiteralNode(type.Description!));
            }
            foreach (OntologyEdgeType type in version.EdgeTypes)
            {
                INode property = g.CreateUriNode(new Uri(types + "relation:" + Uri.EscapeDataString(type.Name)));
                g.Assert(property, a, g.CreateUriNode(new Uri(RdfWriting.Owl + "ObjectProperty")));
                g.Assert(property, label, g.CreateLiteralNode(type.Name));
                if (!String.IsNullOrWhiteSpace(type.Description)) g.Assert(property, comment, g.CreateLiteralNode(type.Description!));
                List<OntologyRule> endpoints = version.Rules.Where(r => r.RuleType == OntologyRuleTypeEnum.EdgeEndpoints && OntologyTypeResolver.Same(r.EdgeType, type.Name)).ToList();
                if (endpoints.Count == 1)
                {
                    g.Assert(property, g.CreateUriNode(new Uri(RdfWriting.Rdfs + "domain")), g.CreateUriNode(new Uri(types + "type:" + Uri.EscapeDataString(endpoints[0].FromNodeType ?? String.Empty))));
                    g.Assert(property, g.CreateUriNode(new Uri(RdfWriting.Rdfs + "range")), g.CreateUriNode(new Uri(types + "type:" + Uri.EscapeDataString(endpoints[0].ToNodeType ?? String.Empty))));
                }
                else if (endpoints.Count > 1)
                {
                    g.Assert(property, comment, g.CreateLiteralNode("Allowed: " + String.Join(", ", endpoints.Select(e => e.FromNodeType + " -> " + e.ToNodeType))));
                }
            }

            if (version.Concepts.Count > 0)
            {
                INode scheme = g.CreateUriNode(new Uri(root + ":taxonomy"));
                g.Assert(scheme, a, g.CreateUriNode(new Uri(RdfWriting.Skos + "ConceptScheme")));
                g.Assert(scheme, g.CreateUriNode(new Uri(RdfWriting.Skos + "prefLabel")), g.CreateLiteralNode(ontology.Name + " taxonomy"));
                foreach (OntologyConcept concept in version.Concepts)
                {
                    INode node = ConceptNode(g, root, concept.Key);
                    g.Assert(node, a, g.CreateUriNode(new Uri(RdfWriting.Skos + "Concept")));
                    g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "inScheme")), scheme);
                    g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "prefLabel")), g.CreateLiteralNode(concept.PrefLabel));
                    g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "notation")), g.CreateLiteralNode(concept.Key));
                    foreach (string alt in concept.AltLabels) g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "altLabel")), g.CreateLiteralNode(alt));
                    if (!String.IsNullOrWhiteSpace(concept.Definition)) g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "definition")), g.CreateLiteralNode(concept.Definition!));
                    if (!String.IsNullOrWhiteSpace(concept.BroaderKey)) g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "broader")), ConceptNode(g, root, concept.BroaderKey!));
                    else g.Assert(node, g.CreateUriNode(new Uri(RdfWriting.Skos + "topConceptOf")), scheme);
                }
            }
            return g;
        }

        private static string GraphExporterTypes(string? baseIri)
        {
            return String.IsNullOrWhiteSpace(baseIri) ? GraphExporter.DefaultBaseIri : baseIri!;
        }

        private static INode ConceptNode(Graph g, string root, string key)
        {
            Uri? absolute;
            if (Uri.TryCreate(key, UriKind.Absolute, out absolute) && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps || absolute.Scheme == "urn"))
                return g.CreateUriNode(absolute);
            return g.CreateUriNode(new Uri(root + ":concept:" + Uri.EscapeDataString(key)));
        }

        #endregion
    }
}
