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
                                    new CandidateNode { Ref = "n1", NodeType = "Record", Name = "It Takes a Nation" },
                                    new CandidateNode { Ref = "n2", NodeType = "Track", Name = "Bring the Noise" }
                                },
                                Edges = new List<CandidateEdge>
                                {
                                    new CandidateEdge { FromRef = "n1", ToRef = "n2", EdgeType = "HAS_TRACK" }
                                }
                            };

                            MergeResult result = await merger.MergeAsync(sub, "ten_x", "sub_x", source.Id, "job_x", ct);
                            if (result.NodeIds.Count < 2) throw new Exception("Expected at least 2 merged nodes");
                            // 1 HAS_TRACK + 2 DERIVED_FROM_SOURCE provenance edges = 3
                            if (graph.EdgeCount < 3) throw new Exception("Expected relationship + provenance edges, got " + graph.EdgeCount);
                        }),

                    new TestCaseDescriptor("Graph", "Merge_DedupesByCanonical", "Merge reuses an existing node with the same canonical name",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            SubgraphMerger merger = new SubgraphMerger(graph);

                            CandidateSubgraph first = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "a", NodeType = "Person", Name = "Chuck D", CanonicalName = "Chuck D" } }
                            };
                            await merger.MergeAsync(first, "ten_x", "sub_x", null, "job_1", ct);
                            int afterFirst = graph.NodeCount;

                            CandidateSubgraph second = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "b", NodeType = "Person", Name = "Chuck D", CanonicalName = "Chuck D" } }
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
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "n1", NodeType = "Track", Name = "Song" } },
                                Edges = new List<CandidateEdge> { new CandidateEdge { FromRef = "n1", ToRef = "missing", EdgeType = "HAS_TRACK" } }
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
                        })
                });
        }
    }
}
