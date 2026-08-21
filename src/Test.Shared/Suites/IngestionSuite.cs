namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Services;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Ingestion pipeline state-machine tests using in-memory integration fakes and a real database.
    /// Classification degrades to an empty subgraph (no live model), which the pipeline handles. Chunk
    /// documents (content + embedding) are stored in a RecallDB collection that ingestion requires.
    /// </summary>
    public static class IngestionSuite
    {
        /// <summary>Build the ingestion suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "Ingestion",
                displayName: "Ingestion Pipeline",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("Ingestion", "UnknownType_Fails", "Unknown document type fails at type detection",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Unknown"), recall, graph);

                            Context context = await SeedJobAsync(db, recall, true, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            IngestionJob after = await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) ?? throw new Exception("job gone");
                            if (after.Status != IngestionStatusEnum.Failed) throw new Exception("expected Failed, got " + after.Status);
                            if (after.Stage != IngestionStageEnum.TypeDetection) throw new Exception("expected failure at TypeDetection, got " + after.Stage);

                            SubjectLink link = await db.SubjectLinks.ReadAsync(context.TenantId, context.LinkId, ct) ?? throw new Exception("link gone");
                            if (link.Status != SubjectLinkStatusEnum.Failed) throw new Exception("link should be Failed");
                            if (String.IsNullOrEmpty(link.LastError)) throw new Exception("link should carry an error");
                        }),

                    new TestCaseDescriptor("Ingestion", "NoCollection_FailsAtIndexing", "A job with no collection assigned fails at the indexing stage",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);
                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Text"), recall, graph);

                            // Intentionally seed a job with no CollectionId to prove indexing enforces the requirement.
                            Context context = await SeedJobAsync(db, recall, false, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            IngestionJob after = await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) ?? throw new Exception("job gone");
                            if (after.Status != IngestionStatusEnum.Failed) throw new Exception("expected Failed, got " + after.Status + " (" + after.Error + ")");
                            if (after.Stage != IngestionStageEnum.Indexing) throw new Exception("expected failure at Indexing, got " + after.Stage);
                            if (recall.DocumentCount != 0) throw new Exception("no chunk documents should be stored without a collection");
                        }),

                    new TestCaseDescriptor("Ingestion", "HappyPath_Completes", "A known document runs through all stages to Completed",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Text"), recall, graph);

                            Context context = await SeedJobAsync(db, recall, true, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            IngestionJob after = await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) ?? throw new Exception("job gone");
                            if (after.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + after.Status + " (" + after.Error + ")");
                            if (after.Stage != IngestionStageEnum.Done) throw new Exception("expected stage Done");
                            if (after.GraphNodeIds.Count < 1) throw new Exception("expected at least the Source graph node");
                            if (after.CollectionId != context.CollectionId) throw new Exception("job should record its collection id");
                            if (graph.NodeCount < 1) throw new Exception("no graph node was created");
                            if (recall.DocumentCount < 1) throw new Exception("no chunk document was stored in the collection");

                            // Chunks are modeled as first-class Chunk nodes linked to their source.
                            List<GraphNode> chunkNodes = await graph.SearchNodesByTagsAsync(new Dictionary<string, string> { { "nodeType", "Chunk" } }, 100, ct);
                            if (chunkNodes.Count < 1) throw new Exception("expected at least one Chunk graph node");

                            SubjectLink link = await db.SubjectLinks.ReadAsync(context.TenantId, context.LinkId, ct) ?? throw new Exception("link gone");
                            if (link.Status != SubjectLinkStatusEnum.Ingested) throw new Exception("link should be Ingested");
                            if (link.LastIngestedUtc == null) throw new Exception("link should record LastIngestedUtc");

                            List<IngestionJobEvent> events = await db.IngestionJobEvents.EnumerateByJobAsync(context.TenantId, claimed.Id, ct);
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Done)) throw new Exception("expected a Done event");

                            // The per-step ingestion log must record each pipeline step.
                            string log = String.Join(" | ", events.ConvertAll(e => e.Message ?? String.Empty));
                            string[] expectedSteps =
                            {
                                "Type detection", "Semantic cell extraction", "Ontology", "Knowledge-graph insertion",
                                "Summarization", "Chunking", "Embedding", "Search indexing"
                            };
                            foreach (string step in expectedSteps)
                            {
                                if (!log.Contains(step)) throw new Exception("ingestion log missing step '" + step + "'. Log: " + log);
                            }

                            // Summarization, chunking, and embedding must be recorded as three discrete stages.
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Summarization)) throw new Exception("expected a Summarization stage event");
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Chunking)) throw new Exception("expected a Chunking stage event");
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Embedding)) throw new Exception("expected an Embedding stage event");

                            // The two-stage boundary (categorize then hydrate) must be visible in the Follow-Logs stream.
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Categorization)) throw new Exception("expected a Categorization phase event");
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Hydration)) throw new Exception("expected a Hydration phase event");
                            if (!log.Contains("Categorization complete")) throw new Exception("ingestion log missing the candidate-plan (Categorization) event. Log: " + log);
                            if (!log.Contains("Hydration started")) throw new Exception("ingestion log missing the Hydration event. Log: " + log);
                            if (!log.Contains("Prompt provenance")) throw new Exception("ingestion log missing the prompt-provenance (reproducibility) event. Log: " + log);

                            // Every stage event is stamped with its parent job's subject id (denormalized for
                            // subject-scoped activity aggregation).
                            foreach (IngestionJobEvent ev in events)
                            {
                                if (ev.SubjectId != context.SubjectId) throw new Exception("stage event missing/incorrect SubjectId");
                            }

                            // The ingestion activity summary buckets those events by stage over time.
                            IngestionActivityFilter summaryFilter = new IngestionActivityFilter
                            {
                                TenantId = context.TenantId,
                                FromUtc = DateTime.UtcNow.AddHours(-1),
                                ToUtc = DateTime.UtcNow.AddMinutes(1),
                                BucketMinutes = 5
                            };
                            IngestionActivitySummary summary = await db.IngestionJobEvents.SummarizeAsync(summaryFilter, ct);
                            if (summary.TotalCount != events.Count) throw new Exception("summary total (" + summary.TotalCount + ") should equal event count (" + events.Count + ")");
                            if (!summary.Totals.Exists(s => s.Stage == IngestionStageEnum.Done && s.Count >= 1)) throw new Exception("summary totals should include a Done stage");
                            long bucketSum = 0;
                            foreach (IngestionActivityBucket bucket in summary.Buckets) bucketSum += bucket.TotalCount;
                            if (bucketSum != summary.TotalCount) throw new Exception("summary bucket counts should sum to the total");

                            // The subject filter scopes the summary: the job's own subject sees all events; another does not.
                            summaryFilter.SubjectId = context.SubjectId;
                            IngestionActivitySummary scoped = await db.IngestionJobEvents.SummarizeAsync(summaryFilter, ct);
                            if (scoped.TotalCount != events.Count) throw new Exception("subject-scoped summary should match the subject's events");
                            summaryFilter.SubjectId = "sub_does_not_exist";
                            IngestionActivitySummary empty = await db.IngestionJobEvents.SummarizeAsync(summaryFilter, ct);
                            if (empty.TotalCount != 0) throw new Exception("summary for an unknown subject should be empty");
                        }),

                    new TestCaseDescriptor("Ingestion", "CascadeDelete_RemovesArtifacts", "Deleting a subject cascades through links, jobs, logs, graph nodes, and stored chunk documents",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            FakeRecallDbClient recall = new FakeRecallDbClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Text"), recall, graph);

                            Context context = await SeedJobAsync(db, recall, true, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            // Precondition: ingestion produced a graph node, a stored chunk document, and a processing log.
                            if (graph.NodeCount < 1) throw new Exception("precondition: expected at least one graph node");
                            if (recall.DocumentCount < 1) throw new Exception("precondition: expected at least one stored chunk document");
                            List<IngestionJobEvent> before = await db.IngestionJobEvents.EnumerateByJobAsync(context.TenantId, claimed.Id, ct);
                            if (before.Count < 1) throw new Exception("precondition: expected job events");

                            // Cascade delete the whole subject via the shared service used by the routes.
                            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
                            CascadeDeletionService cascade = new CascadeDeletionService(db, new NullArtifactStore(), recall, new FakeGraphRepositoryFactory(graph), blobs);
                            bool deleted = await cascade.DeleteSubjectCascadeAsync(context.TenantId, context.SubjectId, ct);
                            if (!deleted) throw new Exception("subject delete returned false");

                            // Postcondition: subject and every subordinate object were cascaded away.
                            if (graph.NodeCount != 0) throw new Exception("graph nodes not cascaded, remaining: " + graph.NodeCount);
                            if (graph.EdgeCount != 0) throw new Exception("graph edges not cascaded, remaining: " + graph.EdgeCount);
                            if (recall.DocumentCount != 0) throw new Exception("stored chunk documents not cascaded, remaining: " + recall.DocumentCount);
                            List<IngestionJobEvent> afterEvents = await db.IngestionJobEvents.EnumerateByJobAsync(context.TenantId, claimed.Id, ct);
                            if (afterEvents.Count != 0) throw new Exception("job events not cascaded, remaining: " + afterEvents.Count);
                            if (await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) != null) throw new Exception("ingestion job not deleted");
                            if (await db.SubjectLinks.ReadAsync(context.TenantId, context.LinkId, ct) != null) throw new Exception("link not deleted");
                            if (await db.Subjects.ReadAsync(context.TenantId, context.SubjectId, ct) != null) throw new Exception("subject not deleted");
                        })
                });
        }

        private static IngestionProcessor BuildProcessor(DatabaseDriverBase db, FakeDocumentAtomClient docAtom, FakeRecallDbClient recall, FakeLiteGraphClient graph)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
            Pneuma.Server.Settings.IngestionSettings settings = new Pneuma.Server.Settings.IngestionSettings();
            Pneuma.Server.Settings.TelemetrySettings telemetrySettings = new Pneuma.Server.Settings.TelemetrySettings { Enabled = false };
            Pneuma.Server.Services.TelemetryService telemetry = new Pneuma.Server.Services.TelemetryService(telemetrySettings, logging);
            return new IngestionProcessor(db, docAtom, new FakePartioClient(), new FakeGraphRepositoryFactory(graph), recall, blobs, new NullArtifactStore(), new FakeContentFetcher(), cipher, settings, new Pneuma.Server.Settings.RetrievalSettings(), logging, telemetry);
        }

        // Seed a tenant/subject/link/job. When createCollection is true, a collection is created in the
        // (RecallDB) fake under the tenant and assigned to the job, so ingestion has a valid target.
        private static async Task<Context> SeedJobAsync(DatabaseDriverBase db, FakeRecallDbClient recall, bool createCollection, System.Threading.CancellationToken ct)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "IngestTenant" }, ct);
            Subject subject = await db.Subjects.CreateAsync(new Subject { TenantId = tenant.Id, DisplayName = "Chuck D" }, ct);
            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = tenant.Id, SubjectId = subject.Id, Url = "https://example.com/artifact" }, ct);

            string? collectionId = null;
            if (createCollection)
            {
                await recall.EnsureTenantAsync(tenant.Id, tenant.Name, ct);
                RecallCollection collection = await recall.CreateCollectionAsync(tenant.Id, new RecallCollection { Name = "test", Dimensionality = 8 }, ct);
                collectionId = collection.Id;
            }

            await db.IngestionJobs.CreateAsync(new IngestionJob
            {
                TenantId = tenant.Id, SubjectId = subject.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Queued, CollectionId = collectionId
            }, ct);
            return new Context { TenantId = tenant.Id, SubjectId = subject.Id, LinkId = link.Id, CollectionId = collectionId };
        }

        private sealed class Context
        {
            public string TenantId { get; set; } = String.Empty;
            public string SubjectId { get; set; } = String.Empty;
            public string LinkId { get; set; } = String.Empty;
            public string? CollectionId { get; set; } = null;
        }
    }
}
