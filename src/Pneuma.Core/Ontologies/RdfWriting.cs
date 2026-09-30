namespace Pneuma.Core.Ontologies
{
    using System;
    using System.IO;
    using VDS.RDF;
    using VDS.RDF.JsonLd;
    using VDS.RDF.Parsing;
    using VDS.RDF.Writing;

    /// <summary>
    /// RDF vocabulary IRIs and the serialization and parsing helpers the exporters and the SKOS importer share. JSON-LD
    /// parsing never loads remote documents or contexts, so an uploaded file cannot make the server fetch a URL.
    /// </summary>
    public static class RdfWriting
    {
        #region Public-Members

        /// <summary>RDF namespace.</summary>
        public const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";

        /// <summary>RDF Schema namespace.</summary>
        public const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";

        /// <summary>OWL namespace.</summary>
        public const string Owl = "http://www.w3.org/2002/07/owl#";

        /// <summary>SKOS namespace.</summary>
        public const string Skos = "http://www.w3.org/2004/02/skos/core#";

        /// <summary>PROV-O namespace.</summary>
        public const string Prov = "http://www.w3.org/ns/prov#";

        /// <summary>XML Schema datatypes namespace.</summary>
        public const string Xsd = "http://www.w3.org/2001/XMLSchema#";

        #endregion

        #region Public-Methods

        /// <summary>Write a graph as Turtle.</summary>
        /// <param name="graph">The graph.</param>
        /// <returns>The Turtle text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> is null.</exception>
        public static string Turtle(IGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            using (System.IO.StringWriter writer = new System.IO.StringWriter())
            {
                new CompressingTurtleWriter().Save(graph, writer);
                return writer.ToString();
            }
        }

        /// <summary>Write a graph as JSON-LD.</summary>
        /// <param name="graph">The graph.</param>
        /// <returns>The JSON-LD text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> is null.</exception>
        public static string JsonLd(IGraph graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            TripleStore store = new TripleStore();
            store.Add(graph, true);
            using (System.IO.StringWriter writer = new System.IO.StringWriter())
            {
                new JsonLdWriter().Save(store, writer, true);
                return writer.ToString();
            }
        }

        /// <summary>Parse Turtle or JSON-LD into a single merged graph.</summary>
        /// <param name="text">The document.</param>
        /// <param name="jsonLd">True for JSON-LD, false for Turtle.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="text"/> is null.</exception>
        /// <exception cref="RdfParseException">Thrown when the document is not valid.</exception>
        public static IGraph Parse(string text, bool jsonLd)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            Graph merged = new Graph();
            if (!jsonLd)
            {
                using (StringReader reader = new StringReader(text))
                {
                    new TurtleParser().Load(merged, reader);
                }
                return merged;
            }

            JsonLdProcessorOptions options = new JsonLdProcessorOptions
            {
                RemoteContextLimit = 0,
                DocumentLoader = RefuseRemote
            };
            TripleStore store = new TripleStore();
            using (StringReader reader = new StringReader(text))
            {
                new JsonLdParser(options).Load(store, reader);
            }
            foreach (IGraph graph in store.Graphs) merged.Merge(graph);
            return merged;
        }

        #endregion

        #region Private-Methods

        private static RemoteDocument RefuseRemote(Uri uri, JsonLdLoaderOptions options)
        {
            throw new InvalidOperationException("Remote JSON-LD documents and contexts are not loaded (" + uri + ").");
        }

        #endregion
    }
}
