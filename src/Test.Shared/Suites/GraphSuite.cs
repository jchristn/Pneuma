namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Subgraph merge + entity-resolution tests against an in-memory graph.
    /// </summary>
    public static class GraphSuite
    {
        /// <summary>Build the graph suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Graph",
                displayName: "Graph Merge",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Graph", "Merge_CreatesNodesAndEdges", "Merge creates nodes, edges, and provenance",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);
                            GraphNode source = await graph.CreateNodeAsync(new GraphNode { NodeType = "Source", Name = "src" }, ct);

                            CandidateSubgraph sub = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode>
                                {
                                    new CandidateNode { Ref = "n1", NodeType = "Collection", Name = "Example Collection" },
                                    new CandidateNode { Ref = "n2", NodeType = "Work", Name = "Example Work" }
                                },
                                Edges = new List<CandidateEdge>
                                {
                                    new CandidateEdge { FromRef = "n1", ToRef = "n2", EdgeType = "HAS_PART" }
                                }
                            };

                            MergeResult result = await merger.MergeAsync(sub, "ten_x", "sub_x", source.Id, "job_x", ct);
                            if (result.NodeIds.Count < 2) throw new Exception("Expected at least 2 merged nodes");
                            // 1 HAS_PART + 2 DERIVED_FROM_SOURCE provenance edges = 3
                            if (graph.EdgeCount < 3) throw new Exception("Expected relationship + provenance edges, got " + graph.EdgeCount);
                        }),

                    new TestCaseDescriptor("Graph", "Merge_DedupesByCanonical", "Merge reuses an existing node with the same canonical name",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);

                            CandidateSubgraph first = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "a", NodeType = "Person", Name = "Example Person", CanonicalName = "Example Person" } }
                            };
                            await merger.MergeAsync(first, "ten_x", "sub_x", null, "job_1", ct);
                            int afterFirst = graph.NodeCount;

                            CandidateSubgraph second = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "b", NodeType = "Person", Name = "Example Person", CanonicalName = "Example Person" } }
                            };
                            await merger.MergeAsync(second, "ten_x", "sub_x", null, "job_2", ct);

                            if (graph.NodeCount != afterFirst) throw new Exception("Duplicate canonical entity was not resolved to the existing node");
                        }),

                    new TestCaseDescriptor("Graph", "Merge_SkipsEdgesWithUnknownRefs", "Edges referencing missing nodes are skipped",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);
                            CandidateSubgraph sub = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "n1", NodeType = "Work", Name = "Example Work" } },
                                Edges = new List<CandidateEdge> { new CandidateEdge { FromRef = "n1", ToRef = "missing", EdgeType = "HAS_PART" } }
                            };
                            await merger.MergeAsync(sub, "ten_x", "sub_x", null, "job_x", ct);
                            if (graph.EdgeCount != 0) throw new Exception("Edge with an unresolved ref should have been skipped");
                        }),

                    new TestCaseDescriptor("Graph", "Merge_EmptySubgraph_NoOp", "Merging an empty subgraph creates nothing",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);
                            MergeResult result = await merger.MergeAsync(
                                new CandidateSubgraph { Nodes = new List<CandidateNode>(), Edges = new List<CandidateEdge>() },
                                "ten_x", "sub_x", null, "job_empty", ct);
                            if (graph.NodeCount != 0) throw new Exception("empty subgraph should create no nodes");
                            if (graph.EdgeCount != 0) throw new Exception("empty subgraph should create no edges");
                            if (result.NodeIds.Count != 0) throw new Exception("empty merge should report no node ids");
                        }),

                    new TestCaseDescriptor("Graph", "Ontology_Canonicalizes", "Ontology canonicalization coerces known type variants and preserves custom ones",
                        executeAsync: ct =>
                        {
                            if (Ontology.CanonicalNodeType("organisation") != Ontology.NodeOrganization) throw new Exception("British 'organisation' should coerce to Organization");
                            if (Ontology.CanonicalNodeType("  PERSON ") != Ontology.NodePerson) throw new Exception("casing/whitespace should coerce to Person");
                            if (Ontology.CanonicalNodeType("topic") != Ontology.NodeTopic) throw new Exception("'topic' should coerce to Topic");
                            if (Ontology.CanonicalNodeType("Gene") != "Gene") throw new Exception("a custom node type should be preserved");
                            if (Ontology.CanonicalEdgeType("has part") != Ontology.EdgeHasPart) throw new Exception("'has part' should coerce to HAS_PART");
                            if (Ontology.CanonicalEdgeType("created-by") != Ontology.EdgeCreatedBy) throw new Exception("'created-by' should coerce to CREATED_BY");
                            if (Ontology.CanonicalEdgeType("works with") != "WORKS_WITH") throw new Exception("a custom edge should normalize to UPPER_SNAKE");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("Graph", "Merge_CanonicalizesTypes", "Merge coerces a known node/edge type variant to its canonical ontology form",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);
                            CandidateSubgraph sub = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode>
                                {
                                    new CandidateNode { Ref = "n1", NodeType = "organisation", Name = "ACME" },
                                    new CandidateNode { Ref = "n2", NodeType = "work", Name = "Widget" }
                                },
                                Edges = new List<CandidateEdge> { new CandidateEdge { FromRef = "n1", ToRef = "n2", EdgeType = "published by" } }
                            };
                            MergeResult result = await merger.MergeAsync(sub, "ten_x", "sub_x", null, "job_x", ct);
                            GraphNode? org = await graph.ReadNodeAsync(result.NodeIds[0], ct);
                            if (org == null || org.NodeType != Ontology.NodeOrganization) throw new Exception("node type should be canonicalized to Organization, got " + (org?.NodeType ?? "null"));
                            List<GraphEdge> edges = await graph.GetEdgesAsync(result.NodeIds[0], ct);
                            bool hasPublishedBy = false;
                            foreach (GraphEdge edge in edges) { if (edge.EdgeType == Ontology.EdgePublishedBy) hasPublishedBy = true; }
                            if (!hasPublishedBy) throw new Exception("edge type 'published by' should be canonicalized to PUBLISHED_BY");
                        }),

                    new TestCaseDescriptor("Graph", "Merge_ConsolidatesEdgeWeight", "Re-asserting a relationship consolidates into one weighted edge (no duplicate)",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);

                            CandidateSubgraph First()
                            {
                                return new CandidateSubgraph
                                {
                                    Nodes = new List<CandidateNode>
                                    {
                                        new CandidateNode { Ref = "a", NodeType = "Person", Name = "Ada", CanonicalName = "Ada" },
                                        new CandidateNode { Ref = "b", NodeType = "Organization", Name = "Acme", CanonicalName = "Acme" }
                                    },
                                    Edges = new List<CandidateEdge> { new CandidateEdge { FromRef = "a", ToRef = "b", EdgeType = "AFFILIATED_WITH", Confidence = 0.5 } }
                                };
                            }

                            MergeResult r1 = await merger.MergeAsync(First(), "ten_x", "sub_x", null, "job_1", ct);
                            int edgesAfterFirst = graph.EdgeCount;
                            string personId = r1.NodeIds[0];

                            // A second source asserts the same relationship: it must consolidate, not duplicate.
                            await merger.MergeAsync(First(), "ten_x", "sub_x", null, "job_2", ct);
                            if (graph.EdgeCount != edgesAfterFirst) throw new Exception("re-asserting a relationship should not add a duplicate edge; edge count went " + edgesAfterFirst + " -> " + graph.EdgeCount);

                            List<GraphEdge> edges = await graph.GetEdgesAsync(personId, ct);
                            GraphEdge? affiliated = null;
                            foreach (GraphEdge edge in edges) { if (edge.EdgeType == Ontology.EdgeAffiliatedWith) affiliated = edge; }
                            if (affiliated == null) throw new Exception("expected the AFFILIATED_WITH edge");
                            if (!affiliated.Tags.TryGetValue(Ontology.TagCorroborationCount, out string? corr) || corr != "2") throw new Exception("corroboration count should be 2 after two assertions, got " + (corr ?? "null"));
                            if (!affiliated.Tags.TryGetValue(Ontology.TagWeight, out string? weightRaw)
                                || !Double.TryParse(weightRaw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double weight)
                                || Math.Abs(weight - 0.75) > 0.001)
                            {
                                throw new Exception("noisy-OR weight should be 0.75 after two 0.5 assertions, got " + (weightRaw ?? "null"));
                            }
                        })
                });
        }
    }
}
