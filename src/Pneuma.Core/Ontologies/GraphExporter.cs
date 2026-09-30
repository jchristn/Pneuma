namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Xml;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Serialization;
    using VDS.RDF;

    /// <summary>
    /// Serializes a subject's graph as Pneuma JSON, GraphML, Turtle, or JSON-LD. In the RDF forms each node is a resource
    /// typed with its node type, labelled with its name, and linked to its provenance source with <c>prov:wasDerivedFrom</c>;
    /// each edge is a triple. Resources are named under a base IRI (default <c>urn:pneuma:</c>).
    /// </summary>
    public static class GraphExporter
    {
        #region Public-Members

        /// <summary>The default base IRI for exported resources.</summary>
        public const string DefaultBaseIri = "urn:pneuma:";

        #endregion

        #region Public-Methods

        /// <summary>The content type of a format.</summary>
        /// <param name="format">The format.</param>
        /// <returns>The content type.</returns>
        public static string ContentType(GraphExportFormatEnum format)
        {
            switch (format)
            {
                case GraphExportFormatEnum.Turtle: return "text/turtle; charset=utf-8";
                case GraphExportFormatEnum.JsonLd: return "application/ld+json; charset=utf-8";
                case GraphExportFormatEnum.GraphMl: return "application/graphml+xml; charset=utf-8";
                default: return "application/json; charset=utf-8";
            }
        }

        /// <summary>The file extension of a format.</summary>
        /// <param name="format">The format.</param>
        /// <returns>The extension, without a dot.</returns>
        public static string Extension(GraphExportFormatEnum format)
        {
            switch (format)
            {
                case GraphExportFormatEnum.Turtle: return "ttl";
                case GraphExportFormatEnum.JsonLd: return "jsonld";
                case GraphExportFormatEnum.GraphMl: return "graphml";
                default: return "json";
            }
        }

        /// <summary>Serialize a subject's graph.</summary>
        /// <param name="graph">The graph.</param>
        /// <param name="subject">The subject.</param>
        /// <param name="format">The format.</param>
        /// <param name="baseIri">Base IRI for RDF resources, or null for <see cref="DefaultBaseIri"/>.</param>
        /// <returns>The serialized graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> or <paramref name="subject"/> is null.</exception>
        public static string Serialize(SubjectGraph graph, Subject subject, GraphExportFormatEnum format, string? baseIri)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            if (subject == null) throw new ArgumentNullException(nameof(subject));
            switch (format)
            {
                case GraphExportFormatEnum.GraphMl: return GraphMl(graph);
                case GraphExportFormatEnum.Turtle: return RdfWriting.Turtle(BuildRdf(graph, subject, baseIri));
                case GraphExportFormatEnum.JsonLd: return RdfWriting.JsonLd(BuildRdf(graph, subject, baseIri));
                default:
                    return Json.Serialize(new GraphExportDocument
                    {
                        SubjectId = subject.Id,
                        SubjectName = subject.DisplayName,
                        OntologyVersionId = subject.OntologyVersionId,
                        Truncated = graph.Truncated,
                        Nodes = graph.Nodes,
                        Edges = graph.Edges
                    });
            }
        }

        #endregion

        #region Private-Methods

        private static Graph BuildRdf(SubjectGraph source, Subject subject, string? baseIri)
        {
            string root = String.IsNullOrWhiteSpace(baseIri) ? DefaultBaseIri : baseIri!;
            string nodes = root + subject.TenantId + ":node:";
            string types = root + "type:";
            string relations = root + "relation:";
            string vocab = root + "vocab:";

            Graph g = new Graph();
            g.NamespaceMap.AddNamespace("rdfs", new Uri(RdfWriting.Rdfs));
            g.NamespaceMap.AddNamespace("prov", new Uri(RdfWriting.Prov));
            g.NamespaceMap.AddNamespace("xsd", new Uri(RdfWriting.Xsd));
            INode rdfType = g.CreateUriNode(new Uri(RdfWriting.Rdf + "type"));
            INode label = g.CreateUriNode(new Uri(RdfWriting.Rdfs + "label"));
            INode derivedFrom = g.CreateUriNode(new Uri(RdfWriting.Prov + "wasDerivedFrom"));
            INode canonical = g.CreateUriNode(new Uri(vocab + "canonicalName"));
            INode content = g.CreateUriNode(new Uri(vocab + "content"));
            INode confidence = g.CreateUriNode(new Uri(vocab + "confidence"));
            INode subjectProperty = g.CreateUriNode(new Uri(vocab + "subject"));
            INode subjectLiteral = g.CreateLiteralNode(subject.DisplayName);

            foreach (GraphNode node in source.Nodes)
            {
                if (String.IsNullOrEmpty(node.Id)) continue;
                INode resource = g.CreateUriNode(new Uri(nodes + Uri.EscapeDataString(node.Id)));
                g.Assert(resource, rdfType, g.CreateUriNode(new Uri(types + Uri.EscapeDataString(String.IsNullOrEmpty(node.NodeType) ? "Thing" : node.NodeType))));
                if (!String.IsNullOrEmpty(node.Name)) g.Assert(resource, label, g.CreateLiteralNode(node.Name));
                if (!String.IsNullOrEmpty(node.CanonicalName)) g.Assert(resource, canonical, g.CreateLiteralNode(node.CanonicalName));
                if (!String.IsNullOrEmpty(node.Content)) g.Assert(resource, content, g.CreateLiteralNode(node.Content));
                g.Assert(resource, subjectProperty, subjectLiteral);
                string? score;
                double parsed;
                if (node.Tags != null && node.Tags.TryGetValue(Ontology.TagConfidence, out score) && Double.TryParse(score, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                    g.Assert(resource, confidence, g.CreateLiteralNode(parsed.ToString("0.###", CultureInfo.InvariantCulture), new Uri(RdfWriting.Xsd + "decimal")));
                string? sourceId;
                if (node.Tags != null && node.Tags.TryGetValue(Ontology.TagSourceId, out sourceId) && !String.IsNullOrEmpty(sourceId) && sourceId != node.Id)
                    g.Assert(resource, derivedFrom, g.CreateUriNode(new Uri(nodes + Uri.EscapeDataString(sourceId))));
            }
            foreach (GraphEdge edge in source.Edges)
            {
                if (String.IsNullOrEmpty(edge.FromNodeId) || String.IsNullOrEmpty(edge.ToNodeId)) continue;
                INode predicate = String.Equals(edge.EdgeType, Ontology.EdgeDerivedFromSource, StringComparison.Ordinal)
                    ? derivedFrom
                    : g.CreateUriNode(new Uri(relations + Uri.EscapeDataString(String.IsNullOrEmpty(edge.EdgeType) ? "RELATED_TO" : edge.EdgeType)));
                g.Assert(g.CreateUriNode(new Uri(nodes + Uri.EscapeDataString(edge.FromNodeId))), predicate, g.CreateUriNode(new Uri(nodes + Uri.EscapeDataString(edge.ToNodeId))));
            }
            return g;
        }

        private static string GraphMl(SubjectGraph graph)
        {
            XmlWriterSettings settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter xml = XmlWriter.Create(stream, settings))
                {
                    const string ns = "http://graphml.graphdrawing.org/xmlns";
                    xml.WriteStartDocument();
                    xml.WriteStartElement("graphml", ns);
                    WriteKey(xml, ns, "type", "node", "type");
                    WriteKey(xml, ns, "name", "node", "name");
                    WriteKey(xml, ns, "canonicalName", "node", "canonicalName");
                    WriteKey(xml, ns, "content", "node", "content");
                    WriteKey(xml, ns, "tags", "all", "tags");
                    WriteKey(xml, ns, "edgeType", "edge", "type");
                    xml.WriteStartElement("graph", ns);
                    xml.WriteAttributeString("id", "G");
                    xml.WriteAttributeString("edgedefault", "directed");
                    foreach (GraphNode node in graph.Nodes)
                    {
                        if (String.IsNullOrEmpty(node.Id)) continue;
                        xml.WriteStartElement("node", ns);
                        xml.WriteAttributeString("id", node.Id);
                        WriteData(xml, ns, "type", node.NodeType);
                        WriteData(xml, ns, "name", node.Name);
                        WriteData(xml, ns, "canonicalName", node.CanonicalName);
                        WriteData(xml, ns, "content", node.Content);
                        WriteData(xml, ns, "tags", Tags(node.Tags));
                        xml.WriteEndElement();
                    }
                    foreach (GraphEdge edge in graph.Edges)
                    {
                        xml.WriteStartElement("edge", ns);
                        xml.WriteAttributeString("id", edge.Id);
                        xml.WriteAttributeString("source", edge.FromNodeId);
                        xml.WriteAttributeString("target", edge.ToNodeId);
                        WriteData(xml, ns, "edgeType", edge.EdgeType);
                        WriteData(xml, ns, "tags", Tags(edge.Tags));
                        xml.WriteEndElement();
                    }
                    xml.WriteEndElement();
                    xml.WriteEndElement();
                    xml.WriteEndDocument();
                }
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static void WriteKey(XmlWriter xml, string ns, string id, string domain, string name)
        {
            xml.WriteStartElement("key", ns);
            xml.WriteAttributeString("id", id);
            xml.WriteAttributeString("for", domain);
            xml.WriteAttributeString("attr.name", name);
            xml.WriteAttributeString("attr.type", "string");
            xml.WriteEndElement();
        }

        private static void WriteData(XmlWriter xml, string ns, string key, string? value)
        {
            if (String.IsNullOrEmpty(value)) return;
            xml.WriteStartElement("data", ns);
            xml.WriteAttributeString("key", key);
            xml.WriteString(value);
            xml.WriteEndElement();
        }

        private static string Tags(Dictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0) return String.Empty;
            return String.Join("; ", tags.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
        }

        #endregion
    }
}
