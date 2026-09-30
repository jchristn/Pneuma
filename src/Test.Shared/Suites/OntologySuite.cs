namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using System.Xml;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Serialization;
    using Test.Shared.Support;
    using Touchstone.Core;
    using VDS.RDF;

    /// <summary>
    /// Ontology building blocks without a server: the built-in template, version validation, the rendered definition,
    /// rule enforcement on candidates and validation of stored graphs, taxonomy matching, version comparison, SKOS and
    /// OWL round trips, graph export formats, drift comparison, and the classification cache key.
    /// </summary>
    public static class OntologySuite
    {
        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ontology",
                displayName: "Ontology governance building blocks",
                cases: new List<TestCaseDescriptor>
                {
                    Case("Template_IsApprovable", "The built-in Default template has no errors or problems and lists its counts", () =>
                    {
                        OntologyVersion template = OntologyTemplates.Build("default") ?? throw new Exception("template missing");
                        Check(OntologyVersionValidator.Errors(template).Count == 0, "no errors");
                        Check(OntologyVersionValidator.Problems(template).Count == 0, "no problems: " + String.Join(" ", OntologyVersionValidator.Problems(template)));
                        OntologyTemplateInfo info = OntologyTemplates.List().Single();
                        Check(info.NodeTypeCount == template.NodeTypes.Count && info.RuleCount > 0, "the listing counts the template");
                        Check(OntologyTemplates.Build("nope") == null, "an unknown template is null");
                    }),

                    Case("Validator_ReportsErrorsAndProblems", "Duplicate names are errors; undeclared references, bad patterns, missing broader concepts, cycles, and shared labels are problems", () =>
                    {
                        OntologyVersion dup = new OntologyVersion();
                        dup.NodeTypes.Add(new OntologyNodeType { Name = "Person" });
                        dup.NodeTypes.Add(new OntologyNodeType { Name = "per son" });
                        Check(OntologyVersionValidator.Errors(dup).Any(e => e.Contains("more than once")), "normalized duplicate is an error");

                        OntologyVersion bad = OntologyTestData.Governed();
                        bad.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.EdgeEndpoints, EdgeType = "LIKES", FromNodeType = "Person", ToNodeType = "Robot" });
                        bad.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.NamePattern, NodeType = "Person", Pattern = "([" });
                        bad.Rules.Add(new OntologyRule { RuleType = OntologyRuleTypeEnum.RequiredField, NodeType = "Person", Action = OntologyRuleActionEnum.Reverse, Field = OntologyNodeFieldEnum.Rights });
                        bad.Concepts.Add(new OntologyConcept { Key = "a", PrefLabel = "Alpha", BroaderKey = "b" });
                        bad.Concepts.Add(new OntologyConcept { Key = "b", PrefLabel = "Beta", BroaderKey = "a" });
                        bad.Concepts.Add(new OntologyConcept { Key = "c", PrefLabel = "Gamma", BroaderKey = "missing" });
                        bad.Concepts.Add(new OntologyConcept { Key = "d", PrefLabel = "KUBERNETES" });
                        List<string> problems = OntologyVersionValidator.Problems(bad);
                        Check(OntologyVersionValidator.Errors(bad).Count == 0, "the draft is still savable");
                        Check(problems.Any(p => p.Contains("'LIKES' is not declared")), "undeclared edge type");
                        Check(problems.Any(p => p.Contains("'Robot' is not declared")), "undeclared node type");
                        Check(problems.Any(p => p.Contains("not a valid regular expression")), "bad pattern");
                        Check(problems.Any(p => p.Contains("Reverse action applies only")), "reverse on a non-endpoint rule");
                        Check(problems.Any(p => p.Contains("form a cycle")), "broader cycle");
                        Check(problems.Any(p => p.Contains("'missing' that does not exist")), "missing broader concept");
                        Check(problems.Any(p => p.Contains("belongs to more than one concept")), "shared label");
                        Check(OntologyVersionValidator.Problems(new OntologyVersion()).Any(p => p.Contains("at least one node type")), "empty version cannot be approved");
                    }),

                    Case("Renderer_ShowsTypesEndpointsConstraintsAndGuidance", "The definition names every type, the allowed directions, the constraints, and the guidance", () =>
                    {
                        string text = OntologyDefinitionRenderer.Render(OntologyTestData.Governed());
                        Check(text.Contains("- Person: A named individual."), "node type with description");
                        Check(text.Contains("- WORKS_FOR (Person -> Organization): Employment."), "edge type with endpoints");
                        Check(text.Contains("A Work has at most 1 CREATED_BY relationship."), "cardinality constraint");
                        Check(text.Contains("Every Person must have content."), "required field constraint");
                        Check(text.Contains("Use only the node and relationship types listed above"), "closed vocabulary note");
                        Check(text.Contains("Prefer Organization for named teams."), "guidance");
                        Check(!text.Contains("Kubernetes"), "the taxonomy is not rendered");
                    }),

                    Case("RuleEngine_EnforcesCandidate", "Warn keeps, Reverse flips, Drop and Quarantine remove, and edges of a removed node go with it", () =>
                    {
                        OntologyVersion version = OntologyTestData.Governed();
                        CandidateSubgraph subgraph = Json.Deserialize<CandidateSubgraph>(OntologyTestData.GovernedReply) ?? throw new Exception("reply");
                        OntologyTypeResolver types = new OntologyTypeResolver(version);
                        foreach (CandidateNode node in subgraph.Nodes) node.NodeType = types.CanonicalNode(node.NodeType);
                        foreach (CandidateEdge edge in subgraph.Edges) edge.EdgeType = types.CanonicalEdge(edge.EdgeType);
                        OntologyRuleOutcome outcome = new OntologyRuleEngine(version).ApplyToCandidate(subgraph);

                        Check(outcome.Violations.Count == 6, "six violations, got " + outcome.Violations.Count + ": " + String.Join(" | ", outcome.Violations.Select(v => v.Message)));
                        Check(outcome.Warned == 1 && outcome.Reversed == 1 && outcome.Dropped == 2 && outcome.Quarantined == 2, "warned 1, reversed 1, dropped 2, quarantined 2");
                        Check(subgraph.Nodes.Count == 4 && !subgraph.Nodes.Any(n => n.Name == "Mars"), "the undeclared planet is held out");
                        Check(subgraph.Edges.Count == 2, "two relationships remain, got " + subgraph.Edges.Count);
                        Check(subgraph.Edges.Any(e => e.EdgeType == "WORKS_FOR" && e.FromRef == "p2" && e.ToRef == "o1"), "the backwards WORKS_FOR was reversed");
                        OntologyViolation quarantinedNode = outcome.Violations.Single(v => v.ElementKind == OntologyElementKindEnum.Node && v.Status == OntologyViolationStatusEnum.Quarantined);
                        Check(quarantinedNode.RuleType == null && quarantinedNode.NodeName == "Mars", "the undeclared node is recorded without a rule");
                        OntologyViolation cardinality = outcome.Violations.Single(v => v.RuleType == OntologyRuleTypeEnum.MaxOutgoing);
                        Check(cardinality.ToNodeName == "Grace" && cardinality.Status == OntologyViolationStatusEnum.Quarantined, "the weaker extra creator is quarantined");
                    }),

                    Case("RuleEngine_ValidatesStoredGraph", "Stored-graph validation reports without changing anything and skips structural and taxonomy elements", () =>
                    {
                        List<GraphNode> nodes = new List<GraphNode>
                        {
                            Node("n1", "Work", "Book"), Node("n2", "Person", "Ada"), Node("n3", "Person", "Grace"),
                            Node("s1", Ontology.NodeSource, "https://example.com"), Node("c1", Ontology.NodeCell, "text"), Node("t1", "Topic", "Cloud")
                        };
                        nodes[5].Tags[Ontology.TagAssertedBy] = Ontology.AssertedByTaxonomy;
                        List<GraphEdge> edges = new List<GraphEdge>
                        {
                            Edge("e1", "CREATED_BY", "n1", "n2"), Edge("e2", "CREATED_BY", "n1", "n3"),
                            Edge("e3", Ontology.EdgeHasCell, "s1", "c1"), Edge("e4", "ABOUT", "c1", "t1")
                        };
                        edges[3].Tags[Ontology.TagAssertedBy] = Ontology.AssertedByTaxonomy;
                        List<OntologyViolation> violations = new OntologyRuleEngine(OntologyTestData.Governed()).ValidateGraph(nodes, edges);
                        Check(violations.Count(v => v.RuleType == OntologyRuleTypeEnum.MaxOutgoing) == 1, "the two creators break the cardinality rule");
                        Check(violations.Count(v => v.RuleType == OntologyRuleTypeEnum.RequiredField) == 2, "both people lack content");
                        Check(!violations.Any(v => v.NodeType == Ontology.NodeSource || v.NodeType == Ontology.NodeCell || v.NodeName == "Cloud"), "structural and taxonomy nodes are not checked");
                        Check(nodes.Count == 6 && edges.Count == 4, "nothing is removed");
                    }),

                    Case("TaxonomyMatcher_WholeWordsLongestFirst", "Matching uses whole words, ignores case, prefers the longest label, and honors case-sensitive concepts", () =>
                    {
                        List<OntologyConcept> concepts = OntologyTestData.Governed().Concepts;
                        concepts.Add(new OntologyConcept { Key = "it", PrefLabel = "IT", CaseSensitive = true });
                        concepts.Add(new OntologyConcept { Key = "ml", PrefLabel = "Machine learning" });
                        concepts.Add(new OntologyConcept { Key = "machine", PrefLabel = "Machine" });
                        TaxonomyMatcher matcher = new TaxonomyMatcher(concepts);
                        List<TaxonomyMatch> matches = matcher.Match("We run K8S clusters and k8s pods; Kubernetesy is not a word. IT runs machine learning, and it is fun.");
                        Check(matches.Count(m => m.ConceptKey == "k8s") == 2, "the alt labels match (longest first), got " + String.Join(",", matches.Select(m => m.Text)));
                        Check(matches.Any(m => m.Text == "K8S clusters" || m.Text == "K8S cluster") || matches.Any(m => m.Text == "K8S"), "the longer label wins where it fits");
                        Check(matches.Count(m => m.ConceptKey == "it") == 1, "IT matches only in upper case");
                        Check(matches.Any(m => m.ConceptKey == "ml") && !matches.Any(m => m.ConceptKey == "machine"), "machine learning beats machine");
                        Check(!matches.Any(m => m.Text.StartsWith("Kubernetesy", StringComparison.Ordinal)), "no partial-word match");
                        Check(new TaxonomyMatcher(new List<OntologyConcept>()).Match("anything").Count == 0, "an empty taxonomy matches nothing");
                    }),

                    Case("Differ_ReportsChanges", "The diff lists added, removed, and changed types, rules, and concepts", () =>
                    {
                        OntologyVersion before = OntologyTestData.Governed();
                        OntologyVersion after = OntologyTestData.Governed();
                        after.NodeTypes.RemoveAll(t => t.Name == "Topic");
                        after.NodeTypes.Add(new OntologyNodeType { Name = "Place" });
                        after.NodeTypes[0].Description = "Changed.";
                        after.Rules.RemoveAll(r => r.RuleType == OntologyRuleTypeEnum.MaxOutgoing);
                        after.Concepts[1].AltLabels.Add("kube");
                        after.Concepts.Add(new OntologyConcept { Key = "docker", PrefLabel = "Docker" });
                        OntologyVersionDiff diff = OntologyDiffer.Compare(before, after);
                        Check(diff.AddedNodeTypes.SequenceEqual(new[] { "Place" }) && diff.RemovedNodeTypes.SequenceEqual(new[] { "Topic" }), "node types added and removed");
                        Check(diff.ChangedNodeTypes.SequenceEqual(new[] { "Person" }), "a changed description");
                        Check(diff.RemovedRules.Count == 1 && diff.AddedRules.Count == 0, "one rule removed");
                        Check(diff.ChangedConcepts.SequenceEqual(new[] { "k8s" }) && diff.AddedConcepts.SequenceEqual(new[] { "docker" }) && diff.TaxonomyChanged, "concepts changed and added");
                        Check(!OntologyDiffer.Compare(before, OntologyTestData.Governed()).TaxonomyChanged, "identical versions have no taxonomy change");
                    }),

                    Case("Skos_ImportsAndRoundTrips", "SKOS Turtle imports with labels, definitions, and broader links; an exported version re-imports with the same keys in both formats", () =>
                    {
                        List<string> warnings = new List<string>();
                        List<OntologyConcept> imported = SkosImporter.Parse(OntologyTestData.SkosTurtle, RdfFormatEnum.Turtle, warnings);
                        OntologyConcept postgres = imported.Single(c => c.PrefLabel == "PostgreSQL");
                        Check(imported.Count == 2 && warnings.Count == 1, "the unnamed concept is skipped with a warning");
                        Check(postgres.AltLabels.Contains("Postgres") && postgres.Definition == "An open-source database." && postgres.BroaderKey == "http://example.org/c/databases", "labels, definition, and broader");

                        TenantOntology ontology = new TenantOntology { TenantId = "ten_x", Name = "Tech" };
                        OntologyVersion version = OntologyTestData.Governed();
                        foreach (RdfFormatEnum format in new[] { RdfFormatEnum.Turtle, RdfFormatEnum.JsonLd })
                        {
                            string exported = OntologyRdfExporter.Serialize(ontology, version, format, null);
                            List<OntologyConcept> back = SkosImporter.Parse(exported, format, new List<string>());
                            Check(back.Count == 2 && back.Any(c => c.Key == "k8s" && c.BroaderKey == "cloud" && c.AltLabels.Contains("k8s")), format + " round trip keeps keys, labels, and broader links");
                            if (format == RdfFormatEnum.Turtle) Check(exported.Contains("owl:Class") && exported.Contains("owl:ObjectProperty") && exported.Contains("rdfs:domain"), "the OWL part is present");
                        }
                    }),

                    Case("Skos_RejectsBadInputWithoutFetching", "Invalid documents and remote JSON-LD contexts are refused without a network call", () =>
                    {
                        ExpectThrows<ArgumentException>(() => SkosImporter.Parse("this is not turtle", RdfFormatEnum.Turtle, new List<string>()), "bad Turtle");
                        ExpectThrows<ArgumentException>(() => SkosImporter.Parse("{\"@context\":\"http://127.0.0.1:9/ctx.jsonld\",\"@id\":\"x\"}", RdfFormatEnum.JsonLd, new List<string>()), "remote context");
                    }),

                    Case("GraphExporter_WritesEveryFormat", "Pneuma JSON, GraphML, Turtle, and JSON-LD all carry the nodes and edges", () =>
                    {
                        SubjectGraph graph = new SubjectGraph();
                        graph.Nodes.Add(Node("n1", "Person", "Ada"));
                        graph.Nodes.Add(Node("n2", "Organization", "Acme & Co"));
                        graph.Nodes[0].Tags[Ontology.TagConfidence] = "0.900";
                        graph.Edges.Add(Edge("e1", "WORKS_FOR", "n1", "n2"));
                        Subject subject = new Subject { TenantId = "ten_x", DisplayName = "Example" };

                        string json = GraphExporter.Serialize(graph, subject, GraphExportFormatEnum.Json, null);
                        GraphExportDocument? doc = Json.Deserialize<GraphExportDocument>(json);
                        Check(doc != null && doc.Nodes.Count == 2 && doc.Edges.Count == 1, "JSON round trip");

                        XmlDocument xml = new XmlDocument();
                        xml.LoadXml(GraphExporter.Serialize(graph, subject, GraphExportFormatEnum.GraphMl, null));
                        Check(xml.GetElementsByTagName("node").Count == 2 && xml.GetElementsByTagName("edge").Count == 1, "GraphML nodes and edges");

                        IGraph turtle = RdfWriting.Parse(GraphExporter.Serialize(graph, subject, GraphExportFormatEnum.Turtle, "https://example.org/kg/"), false);
                        Check(turtle.Triples.Any(t => t.Predicate.ToString().EndsWith("WORKS_FOR", StringComparison.Ordinal)), "the edge is a triple");
                        Check(turtle.Triples.Any(t => t.Subject.ToString().StartsWith("https://example.org/kg/", StringComparison.Ordinal)), "the base IRI is used");
                        IGraph jsonLd = RdfWriting.Parse(GraphExporter.Serialize(graph, subject, GraphExportFormatEnum.JsonLd, null), true);
                        Check(jsonLd.Triples.Count() == turtle.Triples.Count(), "JSON-LD carries the same triples");
                    }),

                    Case("Drift_ComparesSubstance", "Drift ignores case and order and reports only substantive differences", () =>
                    {
                        CandidateSubgraph a = Json.Deserialize<CandidateSubgraph>(OntologyTestData.GovernedReply)!;
                        CandidateSubgraph b = Json.Deserialize<CandidateSubgraph>(OntologyTestData.GovernedReply.Replace("\"Ada\"", "\"ADA\""))!;
                        Check(!ClassificationDrift.Compare(a, b).Changed, "a case change is not drift");
                        CandidateSubgraph c = Json.Deserialize<CandidateSubgraph>(OntologyTestData.GovernedReply.Replace("\"Acme\"", "\"Initech\""))!;
                        ClassificationDrift drift = ClassificationDrift.Compare(a, c);
                        Check(drift.Changed && drift.Describe().Contains("initech"), "a different entity is drift");
                    }),

                    Case("CacheKey_CoversEveryInput", "The cache key changes with the runner, model, temperature, and prompts, and is stable otherwise", () =>
                    {
                        string key = ClassificationCache.Key("mr_1", "m", 0, "system", "user");
                        Check(key.Length == 64 && key == ClassificationCache.Key("mr_1", "m", 0, "system", "user"), "stable");
                        Check(key != ClassificationCache.Key("mr_2", "m", 0, "system", "user"), "runner");
                        Check(key != ClassificationCache.Key("mr_1", "n", 0, "system", "user"), "model");
                        Check(key != ClassificationCache.Key("mr_1", "m", 0.2, "system", "user"), "temperature");
                        Check(key != ClassificationCache.Key("mr_1", "m", 0, "system2", "user"), "system prompt");
                        Check(key != ClassificationCache.Key("mr_1", "m", 0, "system", "user2"), "user prompt");
                    })
                });
        }

        private static TestCaseDescriptor Case(string id, string description, Action body)
        {
            return new TestCaseDescriptor("Ontology", id, description, executeAsync: ct =>
            {
                body();
                return Task.CompletedTask;
            });
        }

        private static GraphNode Node(string id, string type, string name)
        {
            return new GraphNode { Id = id, NodeType = type, Name = name, CanonicalName = name };
        }

        private static GraphEdge Edge(string id, string type, string from, string to)
        {
            return new GraphEdge { Id = id, EdgeType = type, FromNodeId = from, ToNodeId = to };
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void ExpectThrows<T>(Action action, string what) where T : Exception
        {
            try
            {
                action();
            }
            catch (T)
            {
                return;
            }
            throw new Exception(what + ": expected " + typeof(T).Name);
        }
    }
}
