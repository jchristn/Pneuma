namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Observability;
    using Pneuma.Server.Settings;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Retrieval tests over the shared <see cref="GroundedQueryService"/> with in-memory fakes: the
    /// full-text / vector / hybrid search modes (#7) and Maximal-Marginal-Relevance diversity selection of
    /// grounding passages (#11).
    /// </summary>
    public static class RetrievalSuite
    {
        /// <summary>Build the retrieval suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Retrieval",
                displayName: "Retrieval (search modes + diversity)",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Retrieval", "SearchModes_TextVectorHybrid", "Search honors full-text-only, vector-only, and hybrid modes",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            RecallCollection col = await recall.CreateCollectionAsync("ten_x", new RecallCollection { Name = "kb", Dimensionality = 2 }, ct);
                            await StoreAsync(recall, col.Id, new[]
                            {
                                Doc("k1", "lnk_a", "nodeA", "alpha apple pie", 1f, 0f),
                                Doc("k2", "lnk_b", "nodeB", "beta banana bread", 0f, 1f)
                            }, ct);

                            GroundedQueryService svc = BuildService(db, recall, new RetrievalSettings { DefaultCollectionId = col.Id });

                            // Full-text only: substring match on "apple" resolves to nodeA alone.
                            List<RetrievedChunk> text = await svc.SearchAsync("ten_x", "apple", 10, null, RetrievalModeEnum.FullText, null, null, ct);
                            if (text.Count != 1 || text[0].NodeId != "nodeA") throw new Exception("full-text search should return only nodeA for 'apple', got " + Describe(text));

                            // Vector only: keyword is irrelevant; both docs score against the query embedding.
                            List<RetrievedChunk> vector = await svc.SearchAsync("ten_x", "apple", 10, null, RetrievalModeEnum.Vector, null, null, ct);
                            if (vector.Count != 2) throw new Exception("vector search should return both docs, got " + Describe(vector));

                            // Hybrid: both channels fused; nodeA (in both) outranks nodeB (vector only).
                            List<RetrievedChunk> hybrid = await svc.SearchAsync("ten_x", "apple", 10, null, RetrievalModeEnum.Hybrid, null, null, ct);
                            if (hybrid.Count != 2) throw new Exception("hybrid search should return both docs, got " + Describe(hybrid));
                            if (hybrid[0].NodeId != "nodeA") throw new Exception("hybrid fusion should rank nodeA first, got " + Describe(hybrid));
                        }),

                    new TestCaseDescriptor("Retrieval", "Mmr_DropsNearDuplicatePassages", "MMR selection avoids near-duplicate passages that plain top-k would keep",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            RecallCollection col = await recall.CreateCollectionAsync("ten_x", new RecallCollection { Name = "kb", Dimensionality = 2 }, ct);
                            // Two near-identical top hits (matching the query in BOTH channels) and one distinct
                            // passage that only the vector channel reaches, at a lower rank. Plain top-2-by-score
                            // keeps both duplicates; MMR should trade one duplicate for the distinct passage.
                            // The fake embeds every query as [0.1, 0.2], so the duplicates' [0.1, 0.2] vectors are
                            // the closest and the distinct passage's [1, 0] vector ranks below them.
                            await StoreAsync(recall, col.Id, new[]
                            {
                                Doc("k1", "lnk_a", "nodeA", "the quick brown fox jumps over the lazy dog", 0.1f, 0.2f),
                                Doc("k2", "lnk_b", "nodeB", "the quick brown fox jumps over the lazy dog", 0.1f, 0.2f),
                                Doc("k3", "lnk_c", "nodeC", "an entirely separate unrelated topic sentence", 1f, 0f)
                            }, ct);

                            // Diversity OFF: top-2 by fused score are the two identical passages (both match the
                            // keyword "quick"; the distinct passage does not and ranks below them).
                            GroundedQueryService plain = BuildService(db, recall, new RetrievalSettings { DefaultCollectionId = col.Id, DiversityEnabled = false });
                            List<GraphNode> plainSources = await plain.RetrieveSourcesAsync("ten_x", "quick", 2, null, null, null, ct);
                            if (DistinctContentCount(plainSources) != 1) throw new Exception("without diversity, the two near-duplicate passages should both be kept (1 distinct content), got " + DistinctContentCount(plainSources));

                            // Diversity ON (diversity-leaning lambda): the duplicate is dropped for the distinct passage.
                            GroundedQueryService diverse = BuildService(db, recall, new RetrievalSettings { DefaultCollectionId = col.Id, DiversityEnabled = true, DiversityLambda = 0.3 });
                            List<GraphNode> diverseSources = await diverse.RetrieveSourcesAsync("ten_x", "quick", 2, null, null, null, ct);
                            if (DistinctContentCount(diverseSources) != 2) throw new Exception("with diversity, selection should include the distinct passage (2 distinct contents), got " + DistinctContentCount(diverseSources));
                        }),

                    new TestCaseDescriptor("Retrieval", "CrossEncoderRerank_ReordersByScore", "A cross-encoder reranker reorders candidates by its scores, and falls back cleanly",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();

                            // Scorer gives later passages higher scores, so a descending sort reverses the input.
                            FakeCrossEncoderReranker reranker = new FakeCrossEncoderReranker((query, passages) =>
                            {
                                List<double> scores = new List<double>(passages.Count);
                                for (int i = 0; i < passages.Count; i++) scores.Add(i);
                                return scores;
                            });
                            GroundedQueryService svc = BuildService(db, recall, new RetrievalSettings(), reranker);

                            Subject subject = new Subject { TenantId = "ten_x", DisplayName = "Demo", RerankerType = RerankerTypeEnum.CrossEncoder };
                            List<GraphNode> candidates = new List<GraphNode>
                            {
                                new GraphNode { Id = "n1", Content = "first" },
                                new GraphNode { Id = "n2", Content = "second" },
                                new GraphNode { Id = "n3", Content = "third" }
                            };
                            List<GraphNode> reranked = await svc.RerankAsync("ten_x", subject, "q", candidates, n => n.Content ?? String.Empty, ct);
                            if (reranked.Count != 3 || reranked[0].Id != "n3" || reranked[2].Id != "n1")
                                throw new Exception("cross-encoder should reorder to n3, n2, n1; got " + String.Join(",", reranked.ConvertAll(n => n.Id)));

                            // When the reranker returns no scores and no LLM reranking model is set, order is preserved.
                            FakeCrossEncoderReranker nullReranker = new FakeCrossEncoderReranker((query, passages) => null);
                            GroundedQueryService svc2 = BuildService(db, recall, new RetrievalSettings(), nullReranker);
                            List<GraphNode> unchanged = await svc2.RerankAsync("ten_x", subject, "q", candidates, n => n.Content ?? String.Empty, ct);
                            if (unchanged.Count != 3 || unchanged[0].Id != "n1")
                                throw new Exception("fallback should preserve original order; got " + String.Join(",", unchanged.ConvertAll(n => n.Id)));
                        }),

                    new TestCaseDescriptor("Retrieval", "Subgraph_MultiHopBounded", "Depth-limited subgraph extraction reaches N hops and stops",
                        executeAsync: async ct =>
                        {
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            // Chain A - B - C - D.
                            GraphNode a = await graph.CreateNodeAsync(new GraphNode { NodeType = "Topic", Name = "A" }, ct);
                            GraphNode b = await graph.CreateNodeAsync(new GraphNode { NodeType = "Topic", Name = "B" }, ct);
                            GraphNode c = await graph.CreateNodeAsync(new GraphNode { NodeType = "Topic", Name = "C" }, ct);
                            GraphNode d = await graph.CreateNodeAsync(new GraphNode { NodeType = "Topic", Name = "D" }, ct);
                            await graph.CreateEdgeAsync(new GraphEdge { FromNodeId = a.Id, ToNodeId = b.Id, EdgeType = "ABOUT" }, ct);
                            await graph.CreateEdgeAsync(new GraphEdge { FromNodeId = b.Id, ToNodeId = c.Id, EdgeType = "ABOUT" }, ct);
                            await graph.CreateEdgeAsync(new GraphEdge { FromNodeId = c.Id, ToNodeId = d.Id, EdgeType = "ABOUT" }, ct);

                            GraphSubgraph twoHops = await graph.GetSubgraphAsync(a.Id, 2, 0, 0, ct);
                            HashSet<string> ids = new HashSet<string>();
                            foreach (GraphNode n in twoHops.Nodes) ids.Add(n.Id);
                            if (!ids.Contains(a.Id) || !ids.Contains(b.Id) || !ids.Contains(c.Id)) throw new Exception("2-hop subgraph should reach A, B, C");
                            if (ids.Contains(d.Id)) throw new Exception("2-hop subgraph should NOT reach D (3 hops away)");

                            GraphSubgraph oneHop = await graph.GetSubgraphAsync(a.Id, 1, 0, 0, ct);
                            HashSet<string> oneIds = new HashSet<string>();
                            foreach (GraphNode n in oneHop.Nodes) oneIds.Add(n.Id);
                            if (oneIds.Contains(c.Id)) throw new Exception("1-hop subgraph should not reach C");
                        }),

                    new TestCaseDescriptor("Retrieval", "Communities_BuildAndGlobalQuery", "Community summaries are built per cluster and drive the global query mode",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            Subject subject = new Subject { TenantId = "ten_x", DisplayName = "Communities Demo", EmbeddingModel = "default", InferenceModel = "default", Collection = "col" };
                            await db.Subjects.CreateAsync(subject, ct);

                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            FakeGraphRepositoryFactory factory = new FakeGraphRepositoryFactory(graph);
                            // Two disconnected clusters of 3 entities each → two communities, each at/above the min size.
                            await CreateClusterAsync(graph, subject.Id, "Person", new[] { "Ada", "Grace", "Alan" }, ct);
                            await CreateClusterAsync(graph, subject.Id, "Organization", new[] { "Acme", "Globex", "Initech" }, ct);

                            GroundedQueryService gq = BuildServiceWithFactory(db, factory);
                            CommunityService svc = new CommunityService(db, factory, gq, new RetrievalSettings(), new LoggingModule());

                            int created = await svc.BuildAsync("ten_x", subject.Id, ct);
                            if (created != 2) throw new Exception("expected 2 community summaries (one per cluster), got " + created);

                            List<GraphNode> summaries = await svc.ListSummariesAsync("ten_x", subject.Id, ct);
                            if (summaries.Count != 2) throw new Exception("expected 2 stored community summaries, got " + summaries.Count);
                            foreach (GraphNode summary in summaries)
                            {
                                if (!summary.Tags.TryGetValue(Ontology.TagMemberCount, out string? mc) || mc != "3") throw new Exception("each community should have 3 members, got " + (mc ?? "null"));
                                if (String.IsNullOrWhiteSpace(summary.Content)) throw new Exception("a community summary should have content");
                            }

                            // A rebuild replaces (does not accumulate) summaries.
                            int rebuilt = await svc.BuildAsync("ten_x", subject.Id, ct);
                            if (rebuilt != 2) throw new Exception("rebuild should again yield 2 summaries, got " + rebuilt);
                            if ((await svc.ListSummariesAsync("ten_x", subject.Id, ct)).Count != 2) throw new Exception("rebuild should not accumulate duplicate summaries");

                            // Global query grounds on the community summaries.
                            GroundedAnswer answer = await gq.AnswerGlobalAsync("ten_x", subject.Id, "what are the main themes?", 5, ct);
                            if (!answer.Grounded || answer.InsufficientSupport || answer.Sources.Count == 0) throw new Exception("global query should ground on community summaries");

                            // A subject with no summaries reports insufficient support.
                            GroundedAnswer none = await gq.AnswerGlobalAsync("ten_x", "sub_missing", "x", 5, ct);
                            if (!none.InsufficientSupport) throw new Exception("global query with no summaries should report insufficient support");
                        })
                });
        }

        private static async Task CreateClusterAsync(FakeLiteGraphClient graph, string subjectId, string nodeType, string[] names, CancellationToken ct)
        {
            List<string> ids = new List<string>();
            foreach (string name in names)
            {
                GraphNode node = new GraphNode { NodeType = nodeType, Name = name, CanonicalName = name, Labels = new List<string> { nodeType } };
                node.Tags[Ontology.TagSubjectId] = subjectId;
                node.Tags[Ontology.TagNodeType] = nodeType;
                GraphNode created = await graph.CreateNodeAsync(node, ct);
                ids.Add(created.Id);
            }
            for (int i = 0; i + 1 < ids.Count; i++)
            {
                await graph.CreateEdgeAsync(new GraphEdge { FromNodeId = ids[i], ToNodeId = ids[i + 1], EdgeType = Ontology.EdgeAffiliatedWith }, ct);
            }
        }

        private static GroundedQueryService BuildServiceWithFactory(DatabaseDriverBase db, FakeGraphRepositoryFactory factory)
        {
            FakeRecallDbClient recall = new FakeRecallDbClient();
            FakeSemanticProcessor processor = new FakeSemanticProcessor();
            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
            LoggingModule logging = new LoggingModule();
            return new GroundedQueryService(db, recall, recall, factory, recall, processor, new RetrievalSettings(), cipher, logging);
        }

        private static GroundedQueryService BuildService(DatabaseDriverBase db, FakeRecallDbClient recall, RetrievalSettings retrieval, ICrossEncoderReranker? crossEncoder = null)
        {
            FakeLiteGraphClient graph = new FakeLiteGraphClient();
            FakeGraphRepositoryFactory factory = new FakeGraphRepositoryFactory(graph);
            FakeSemanticProcessor processor = new FakeSemanticProcessor();
            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
            LoggingModule logging = new LoggingModule();
            return new GroundedQueryService(db, recall, recall, factory, recall, processor, retrieval, cipher, logging, crossEncoder);
        }

        private static ChunkDocument Doc(string key, string linkId, string nodeId, string content, float e0, float e1)
        {
            return new ChunkDocument
            {
                DocumentKey = key,
                DocumentId = linkId,
                Position = 0,
                Content = content,
                Embedding = new List<float> { e0, e1 },
                Tags = new Dictionary<string, string>
                {
                    { "litegraphNodeId", nodeId },
                    { "linkId", linkId },
                    { "tenantId", "ten_x" }
                }
            };
        }

        private static async Task StoreAsync(FakeRecallDbClient recall, string collectionId, IEnumerable<ChunkDocument> docs, CancellationToken ct)
        {
            await recall.StoreChunksAsync("ten_x", collectionId, new List<ChunkDocument>(docs), ct);
        }

        private static int DistinctContentCount(List<GraphNode> sources)
        {
            HashSet<string> contents = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphNode node in sources) contents.Add(node.Content ?? String.Empty);
            return contents.Count;
        }

        private static string Describe(List<RetrievedChunk> hits)
        {
            List<string> ids = new List<string>();
            foreach (RetrievedChunk hit in hits) ids.Add(hit.NodeId);
            return "[" + String.Join(", ", ids) + "]";
        }
    }
}
