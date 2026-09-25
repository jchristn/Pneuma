namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Caching;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Per-stage contract suite: exercises the success path and the failure path of every ingestion pipeline
    /// stage (<see cref="IStage"/>) in isolation, now that each step is an independently-testable unit. Deterministic
    /// failures (unknown type, no cells, no completion endpoint, no collection) must surface as
    /// <see cref="IngestionHardFailException"/> (never retried); transient failures (fetch/graph/model errors)
    /// surface as ordinary exceptions; a per-cell summarization failure is non-fatal.
    /// </summary>
    public static class IngestionStagesSuite
    {
        /// <summary>Build the per-stage suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "IngestionStages",
                displayName: "Ingestion Pipeline Stages",
                cases: new List<TestCaseDescriptor>
                {
                    // ---- ContentRetrieval ----
                    new TestCaseDescriptor("IngestionStages", "ContentRetrieval_Success", "Content retrieval fetches bytes, hashes them, and does not short-circuit for a fresh link",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new ContentRetrievalStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.SourceBytes.Length == 0) throw new Exception("no bytes fetched");
                            if (String.IsNullOrEmpty(ctx.ContentHash)) throw new Exception("content hash not computed");
                            if (ctx.CompleteEarly) throw new Exception("a fresh link must not short-circuit");
                            if (!ctx.Message.Contains("Content retrieval")) throw new Exception("missing completion message");
                        }),

                    new TestCaseDescriptor("IngestionStages", "ContentRetrieval_DeltaSkip", "Content retrieval short-circuits when the fetched content is unchanged since the last successful ingestion",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());

                            // First run computes the hash; mark the link Ingested with that hash so the second run matches.
                            await new ContentRetrievalStage(deps).ExecuteAsync(ctx, ct);
                            SubjectLink? link = await db.SubjectLinks.ReadAsync(ctx.Job.TenantId, ctx.Job.LinkId, ct);
                            if (link == null) throw new Exception("link gone");
                            link.Status = SubjectLinkStatusEnum.Ingested;
                            link.ContentHash = ctx.ContentHash;
                            await db.SubjectLinks.UpdateAsync(link, ct);

                            StageContext ctx2 = new StageContext(ctx.Job);
                            await new ContentRetrievalStage(deps).ExecuteAsync(ctx2, ct);
                            if (!ctx2.CompleteEarly) throw new Exception("unchanged content should short-circuit");
                        }),

                    new TestCaseDescriptor("IngestionStages", "ContentRetrieval_FetchFailure_IsTransient", "A fetch error propagates as an ordinary (retryable) exception, not a hard fail",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), fetcher: new ThrowingContentFetcher());
                            await AssertThrowsAsync(() => new ContentRetrievalStage(deps).ExecuteAsync(ctx, ct), hardFail: false, stage: null);
                        }),

                    // ---- TypeDetection ----
                    new TestCaseDescriptor("IngestionStages", "TypeDetection_Success", "Type detection records the detected document type on the job",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.SourceBytes = System.Text.Encoding.UTF8.GetBytes("hello");
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), documentAtom: new FakeDocumentAtomClient("Text"));
                            await new TypeDetectionStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Job.DocumentType != "Text") throw new Exception("document type not recorded");
                        }),

                    new TestCaseDescriptor("IngestionStages", "TypeDetection_Unknown_HardFails", "An unknown, non-text document type is a non-retryable hard fail at TypeDetection",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.SourceBytes = new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x89, 0x00 };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), documentAtom: new FakeDocumentAtomClient("Unknown"));
                            await AssertThrowsAsync(() => new TypeDetectionStage(deps).ExecuteAsync(ctx, ct), hardFail: true, stage: IngestionStageEnum.TypeDetection);
                        }),

                    new TestCaseDescriptor("IngestionStages", "TypeDetection_UnknownText_FallsBackToText", "Valid UTF-8 text the detector reports as Unknown is ingested as Text instead of failing",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.SourceBytes = System.Text.Encoding.UTF8.GetBytes("# Architecture\n\u250c\u2500\u2510 server \u2502\n");
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), documentAtom: new FakeDocumentAtomClient("Unknown"));
                            await new TypeDetectionStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Job.DocumentType != "Text") throw new Exception("valid UTF-8 text should fall back to Text, got " + ctx.Job.DocumentType);
                        }),

                    new TestCaseDescriptor("IngestionStages", "StageRunner_InnerCancellation_IsRequestTimeout", "A cancellation raised inside a stage (an HTTP client timeout) is reported as a request timeout, not a stage timeout",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            LoggingModule logging = new LoggingModule();
                            logging.Settings.EnableConsole = false;
                            StageRunner runner = new StageRunner(db, new IngestionJournal(db, logging), new ConcurrencyManager(new IngestionTuning()), new Pneuma.Core.Observability.TelemetryService(new Pneuma.Core.Observability.TelemetrySettings { Enabled = false }, logging));
                            try
                            {
                                await runner.RunAsync(new InnerTimeoutStage(), ctx, ct);
                                throw new Exception("the stage failure should propagate");
                            }
                            catch (TimeoutException e)
                            {
                                if (!e.Message.Contains("Classification", StringComparison.Ordinal) || !e.Message.Contains("request timeout", StringComparison.Ordinal))
                                    throw new Exception("the timeout should name the stage and point at the request timeout, got: " + e.Message);
                            }
                        }),

                    // ---- CellExtraction ----
                    new TestCaseDescriptor("IngestionStages", "CellExtraction_Success", "Cell extraction produces cells and records the count",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.SourceBytes = System.Text.Encoding.UTF8.GetBytes("some content");
                            ctx.Job.DocumentType = "Text";
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), documentAtom: new FakeDocumentAtomClient("Text"));
                            await new CellExtractionStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Cells.Count < 1) throw new Exception("no cells extracted");
                        }),

                    new TestCaseDescriptor("IngestionStages", "CellExtraction_NoCells_HardFails", "A document with no extractable cells is a non-retryable hard fail at CellExtraction",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.SourceBytes = System.Text.Encoding.UTF8.GetBytes("some content");
                            ctx.Job.DocumentType = "Text";
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), documentAtom: new EmptyCellsDocumentAtomClient());
                            await AssertThrowsAsync(() => new CellExtractionStage(deps).ExecuteAsync(ctx, ct), hardFail: true, stage: IngestionStageEnum.CellExtraction);
                        }),

                    // ---- Classification ----
                    new TestCaseDescriptor("IngestionStages", "Classification_Success", "Classification resolves a completion endpoint and produces a candidate subgraph and provenance",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "Example was founded in 2010." } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new ClassificationStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Subgraph == null) throw new Exception("subgraph should not be null (empty is fine with no live model)");
                            if (String.IsNullOrEmpty(ctx.Provenance)) throw new Exception("prompt provenance not recorded");
                            if (ctx.SubjectName != "Example Subject") throw new Exception("subject name not resolved");
                        }),

                    new TestCaseDescriptor("IngestionStages", "Classification_NoEndpoint_HardFails", "Classification with no completion endpoint is a non-retryable hard fail",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "content" } };

                            // Remove the seeded completion runner so no endpoint can be resolved for this job's tenant.
                            List<ModelRunner> runners = await db.ModelRunners.EnumerateAsync(ctx.Job.TenantId, ct);
                            foreach (ModelRunner runner in runners)
                            {
                                if (runner.Capabilities.Contains(ModelCapabilityEnum.Completion)) await db.ModelRunners.DeleteAsync(runner.Id, ct);
                            }

                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await AssertThrowsAsync(() => new ClassificationStage(deps).ExecuteAsync(ctx, ct), hardFail: true, stage: IngestionStageEnum.Classification);
                        }),

                    // ---- OntologyCanonicalization ----
                    new TestCaseDescriptor("IngestionStages", "OntologyCanonicalization_Success", "Canonicalization normalizes node/edge types in place",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Subgraph = new CandidateSubgraph
                            {
                                Nodes = new List<CandidateNode> { new CandidateNode { Ref = "n1", NodeType = "person", Name = "Ada" } }
                            };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new OntologyCanonicalizationStage(deps).ExecuteAsync(ctx, ct);
                            if (!ctx.Message.Contains("canonicalization")) throw new Exception("missing completion message");
                        }),

                    // ---- GraphMerge ----
                    new TestCaseDescriptor("IngestionStages", "GraphMerge_Success", "Graph merge creates the source node and a cell node per non-empty cell",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "a cell" } };
                            StageDependencies deps = BuildDeps(db, recall, graph);
                            await new GraphMergeStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Merge.NodeIds.Count < 1) throw new Exception("expected at least the source node");
                            if (ctx.Merge.CellNodeIds.Count != ctx.Cells.Count) throw new Exception("cell node ids must align one-to-one with cells");
                            if (graph.NodeCount < 1) throw new Exception("no graph node created");
                        }),

                    new TestCaseDescriptor("IngestionStages", "GraphMerge_GraphError_IsTransient", "A graph error at merge propagates as an ordinary (retryable) exception",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "a cell" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), graphFactory: new ThrowingGraphRepositoryFactory());
                            await AssertThrowsAsync(() => new GraphMergeStage(deps).ExecuteAsync(ctx, ct), hardFail: false, stage: null);
                        }),

                    // ---- RelationshipConsolidation ----
                    new TestCaseDescriptor("IngestionStages", "RelationshipConsolidation_Success", "Relationship consolidation runs against the merged graph",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "a cell" } };
                            StageDependencies deps = BuildDeps(db, recall, graph);
                            await new GraphMergeStage(deps).ExecuteAsync(ctx, ct);
                            await new RelationshipConsolidationStage(deps).ExecuteAsync(ctx, ct);
                            if (!ctx.Message.Contains("Relationship consolidation")) throw new Exception("missing completion message");
                        }),

                    new TestCaseDescriptor("IngestionStages", "RelationshipConsolidation_GraphError_IsTransient", "A graph error at consolidation propagates as an ordinary (retryable) exception",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), graphFactory: new ThrowingGraphRepositoryFactory());
                            await AssertThrowsAsync(() => new RelationshipConsolidationStage(deps).ExecuteAsync(ctx, ct), hardFail: false, stage: null);
                        }),

                    // ---- Summarization ----
                    new TestCaseDescriptor("IngestionStages", "Summarization_Success", "Summarization produces a summary for a substantive cell",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = new string('x', 300) } };
                            ctx.Merge = new MergeResult { CellNodeIds = new List<string> { "node1" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new SummarizationStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Summaries.Count < 1) throw new Exception("expected a summary for a substantive cell");
                        }),

                    new TestCaseDescriptor("IngestionStages", "Summarization_CellFailure_IsNonFatal", "A per-cell summarization error is logged and skipped, not fatal to the stage",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = new string('y', 300) } };
                            ctx.Merge = new MergeResult { CellNodeIds = new List<string> { "node1" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), processor: new FailingSemanticProcessor(failSummarize: true));
                            await new SummarizationStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Summaries.Count != 0) throw new Exception("a failed summary must be skipped, leaving no summaries");
                        }),

                    // ---- Chunking ----
                    new TestCaseDescriptor("IngestionStages", "Chunking_Success", "Chunking produces chunks stamped with their cell node id",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "a chunkable cell" } };
                            ctx.Merge = new MergeResult { CellNodeIds = new List<string> { "node1" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            ctx.Summaries = new List<CellSummary> { new CellSummary { CellNodeId = "node1", Text = "a summary of the cell" } };
                            await new ChunkingStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Chunks.Count < 2) throw new Exception("expected a content chunk and a summary chunk");
                            if (ctx.Chunks[0].CellNodeId != "node1") throw new Exception("chunk not stamped with its cell node id");
                            if (ctx.Chunks[0].Kind != "content") throw new Exception("cell text chunk should be kind 'content'");
                            if (ctx.Chunks[ctx.Chunks.Count - 1].Kind != "summary") throw new Exception("summary chunk should be kind 'summary'");
                        }),

                    new TestCaseDescriptor("IngestionStages", "Chunking_Error_IsTransient", "A chunking error propagates as an ordinary (retryable) exception",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Cells = new List<ExtractedCell> { new ExtractedCell { Type = "Text", Text = "cell" } };
                            ctx.Merge = new MergeResult { CellNodeIds = new List<string> { "node1" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), processor: new FailingSemanticProcessor(failChunk: true));
                            await AssertThrowsAsync(() => new ChunkingStage(deps).ExecuteAsync(ctx, ct), hardFail: false, stage: null);
                        }),

                    // ---- Embedding ----
                    new TestCaseDescriptor("IngestionStages", "Embedding_Success", "Embedding assigns a vector to each chunk",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Chunks = new List<SemanticChunk> { new SemanticChunk { Text = "chunk text", CellNodeId = "node1" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new EmbeddingStage(deps).ExecuteAsync(ctx, ct);
                            if (ctx.Chunks[0].Embeddings == null || ctx.Chunks[0].Embeddings!.Count == 0) throw new Exception("chunk was not embedded");
                        }),

                    new TestCaseDescriptor("IngestionStages", "Embedding_Error_IsTransient", "An embedding error propagates as an ordinary (retryable) exception",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Chunks = new List<SemanticChunk> { new SemanticChunk { Text = "chunk text" } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient(), processor: new FailingSemanticProcessor(failEmbed: true));
                            await AssertThrowsAsync(() => new EmbeddingStage(deps).ExecuteAsync(ctx, ct), hardFail: false, stage: null);
                        }),

                    // ---- Indexing ----
                    new TestCaseDescriptor("IngestionStages", "Indexing_Success", "Indexing stores an embedded chunk as a collection document",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, true, ct);
                            ctx.Merge = new MergeResult { NodeIds = new List<string> { "source-node" } };
                            ctx.Chunks = new List<SemanticChunk> { new SemanticChunk { Text = "chunk", CellNodeId = "node1", Embeddings = new List<float> { 0.1f, 0.2f } } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await new IndexingStage(deps).ExecuteAsync(ctx, ct);
                            if (recall.DocumentCount < 1) throw new Exception("no chunk document stored");
                            Dictionary<string, string> tags = recall.AllDocumentTags()[0];
                            if (!tags.TryGetValue("chunkKind", out string? kind) || kind != "content") throw new Exception("stored chunk should carry chunkKind=content");
                        }),

                    new TestCaseDescriptor("IngestionStages", "Indexing_NoCollection_HardFails", "Indexing with no target collection is a non-retryable hard fail",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            StageContext ctx = await SeedContextAsync(db, recall, false, ct);
                            ctx.Merge = new MergeResult { NodeIds = new List<string> { "source-node" } };
                            ctx.Chunks = new List<SemanticChunk> { new SemanticChunk { Text = "chunk", Embeddings = new List<float> { 0.1f } } };
                            StageDependencies deps = BuildDeps(db, recall, new FakeLiteGraphClient());
                            await AssertThrowsAsync(() => new IndexingStage(deps).ExecuteAsync(ctx, ct), hardFail: true, stage: IngestionStageEnum.Indexing);
                        })
                });
        }

        #region Helpers

        private static StageDependencies BuildDeps(
            DatabaseDriverBase db,
            FakeRecallDbClient recall,
            IGraphRepository graph,
            IContentFetcher? fetcher = null,
            IDocumentAtomClient? documentAtom = null,
            ISemanticProcessor? processor = null,
            IGraphRepositoryFactory? graphFactory = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            IngestionJournal journal = new IngestionJournal(db, logging);
            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
            return new StageDependencies(
                db,
                processor ?? new FakeSemanticProcessor(),
                new Aes256Cipher("test-signing-key"),
                graphFactory ?? new FakeGraphRepositoryFactory(graph),
                recall,
                new NullArtifactStore(),
                fetcher ?? new FakeContentFetcher(),
                documentAtom ?? new FakeDocumentAtomClient("Text"),
                blobs,
                journal,
                new EmbeddingCache(100),
                new ConcurrencyManager(new IngestionTuning()),
                logging);
        }

        private static async Task<StageContext> SeedContextAsync(DatabaseDriverBase db, FakeRecallDbClient recall, bool createCollection, CancellationToken ct)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "StageTenant" }, ct);
            Subject subject = await db.Subjects.CreateAsync(new Subject { TenantId = tenant.Id, DisplayName = "Example Subject" }, ct);
            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = tenant.Id, SubjectId = subject.Id, Url = "https://example.com/artifact" }, ct);

            string? collectionId = null;
            if (createCollection)
            {
                await recall.EnsureTenantAsync(tenant.Id, tenant.Name, ct);
                RecallCollection collection = await recall.CreateCollectionAsync(tenant.Id, new RecallCollection { Name = "test", Dimensionality = 8 }, ct);
                collectionId = collection.Id;
            }

            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
            {
                TenantId = tenant.Id,
                SubjectId = subject.Id,
                LinkId = link.Id,
                SourceUrl = link.Url,
                Status = IngestionStatusEnum.Processing,
                CollectionId = collectionId
            }, ct);
            return new StageContext(job);
        }

        private static async Task AssertThrowsAsync(Func<Task> action, bool hardFail, IngestionStageEnum? stage)
        {
            try
            {
                await action();
            }
            catch (IngestionHardFailException e)
            {
                if (!hardFail) throw new Exception("expected a transient exception but got a hard fail: " + e.Message);
                if (stage.HasValue && e.Stage != stage.Value) throw new Exception("hard fail at wrong stage: expected " + stage.Value + ", got " + e.Stage);
                return;
            }
            catch (Exception)
            {
                if (hardFail) throw new Exception("expected a hard fail (IngestionHardFailException) but got a transient exception");
                return;
            }
            throw new Exception("expected an exception but none was thrown");
        }

        #endregion

        #region Fakes

        private sealed class ThrowingContentFetcher : IContentFetcher
        {
            public Task<byte[]> FetchAsync(string url, CancellationToken token = default)
            {
                throw new InvalidOperationException("simulated fetch failure");
            }
        }

        private sealed class EmptyCellsDocumentAtomClient : IDocumentAtomClient
        {
            public Task<TypeDetectResult> DetectTypeAsync(byte[] data, CancellationToken token = default)
            {
                return Task.FromResult(new TypeDetectResult { Type = "Text", MimeType = "text/plain", Extension = "txt" });
            }

            public Task<List<ExtractedCell>> ExtractCellsAsync(string documentType, byte[] data, CancellationToken token = default)
            {
                return Task.FromResult(new List<ExtractedCell>());
            }
        }

        private sealed class FailingSemanticProcessor : ISemanticProcessor
        {
            private readonly bool _FailSummarize;
            private readonly bool _FailChunk;
            private readonly bool _FailEmbed;

            public FailingSemanticProcessor(bool failSummarize = false, bool failChunk = false, bool failEmbed = false)
            {
                _FailSummarize = failSummarize;
                _FailChunk = failChunk;
                _FailEmbed = failEmbed;
            }

            public Task<SemanticProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
            {
                return Task.FromResult(new SemanticProcessResult());
            }

            public Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default)
            {
                if (_FailSummarize) throw new InvalidOperationException("simulated summarize failure");
                return Task.FromResult("summary of: " + text);
            }

            public Task<List<SemanticChunk>> ChunkAsync(string text, ChunkingOptions? options = null, CancellationToken token = default)
            {
                if (_FailChunk) throw new InvalidOperationException("simulated chunk failure");
                return Task.FromResult(new List<SemanticChunk> { new SemanticChunk { Text = text } });
            }

            public Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
            {
                if (_FailEmbed) throw new InvalidOperationException("simulated embed failure");
                List<List<float>> vectors = new List<List<float>>();
                if (texts != null)
                {
                    foreach (string text in texts) vectors.Add(new List<float> { 0.1f, 0.2f });
                }
                return Task.FromResult(vectors);
            }
        }

        private sealed class ThrowingGraphRepositoryFactory : IGraphRepositoryFactory
        {
            public Task<IGraphRepository> ForTenantAsync(string tenantId, CancellationToken token = default)
            {
                throw new InvalidOperationException("simulated graph unavailable");
            }
        }

        #endregion
    }
}
