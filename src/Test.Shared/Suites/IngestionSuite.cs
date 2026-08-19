namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Services;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// Ingestion pipeline state-machine tests using in-memory integration fakes and a real database.
    /// Classification degrades to an empty subgraph (no live model), which the pipeline handles.
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
                            FakeVerbexClient verbex = new FakeVerbexClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Unknown"), verbex, graph);

                            Context context = await SeedJobAsync(db, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            IngestionJob after = await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) ?? throw new Exception("job gone");
                            if (after.Status != IngestionStatusEnum.Failed) throw new Exception("expected Failed, got " + after.Status);
                            if (after.Stage != IngestionStageEnum.TypeDetection) throw new Exception("expected failure at TypeDetection, got " + after.Stage);

                            SubjectLink link = await db.SubjectLinks.ReadAsync(context.TenantId, context.LinkId, ct) ?? throw new Exception("link gone");
                            if (link.Status != SubjectLinkStatusEnum.Failed) throw new Exception("link should be Failed");
                            if (String.IsNullOrEmpty(link.LastError)) throw new Exception("link should carry an error");
                        }),

                    new TestCaseDescriptor("Ingestion", "HappyPath_Completes", "A known document runs through all stages to Completed",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            FakeVerbexClient verbex = new FakeVerbexClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Text"), verbex, graph);

                            Context context = await SeedJobAsync(db, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            IngestionJob after = await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) ?? throw new Exception("job gone");
                            if (after.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + after.Status + " (" + after.Error + ")");
                            if (after.Stage != IngestionStageEnum.Done) throw new Exception("expected stage Done");
                            if (after.GraphNodeIds.Count < 1) throw new Exception("expected at least the Source graph node");
                            if (after.VerbexDocumentIds.Count < 1) throw new Exception("expected at least one indexed chunk");
                            if (graph.NodeCount < 1) throw new Exception("no graph node was created");
                            if (verbex.DocumentCount < 1) throw new Exception("no document was indexed");

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
                                "Chunking", "Embedding generation", "Search indexing"
                            };
                            foreach (string step in expectedSteps)
                            {
                                if (!log.Contains(step)) throw new Exception("ingestion log missing step '" + step + "'. Log: " + log);
                            }

                            // The two-stage boundary (categorize then hydrate) must be visible in the Follow-Logs stream.
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Categorization)) throw new Exception("expected a Categorization phase event");
                            if (!events.Exists(e => e.Stage == IngestionStageEnum.Hydration)) throw new Exception("expected a Hydration phase event");
                            if (!log.Contains("Categorization complete")) throw new Exception("ingestion log missing the candidate-plan (Categorization) event. Log: " + log);
                            if (!log.Contains("Hydration started")) throw new Exception("ingestion log missing the Hydration event. Log: " + log);
                            if (!log.Contains("Prompt provenance")) throw new Exception("ingestion log missing the prompt-provenance (reproducibility) event. Log: " + log);
                        }),

                    new TestCaseDescriptor("Ingestion", "CascadeDelete_RemovesArtifacts", "Deleting a subject cascades through links, jobs, logs, graph nodes, and indexed documents",
                        executeAsync: async ct =>
                        {
                            await using DatabaseDriverBase db = await TestDatabase.CreateAsync(ct);

                            FakeVerbexClient verbex = new FakeVerbexClient();
                            FakeLiteGraphClient graph = new FakeLiteGraphClient();
                            IngestionProcessor processor = BuildProcessor(db, new FakeDocumentAtomClient("Text"), verbex, graph);

                            Context context = await SeedJobAsync(db, ct);
                            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
                            await processor.ProcessAsync(claimed, ct);

                            // Precondition: ingestion produced a graph node, an indexed document, and a processing log.
                            if (graph.NodeCount < 1) throw new Exception("precondition: expected at least one graph node");
                            if (verbex.DocumentCount < 1) throw new Exception("precondition: expected at least one indexed document");
                            List<IngestionJobEvent> before = await db.IngestionJobEvents.EnumerateByJobAsync(context.TenantId, claimed.Id, ct);
                            if (before.Count < 1) throw new Exception("precondition: expected job events");

                            // Cascade delete the whole subject via the shared service used by the routes.
                            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
                            CascadeDeletionService cascade = new CascadeDeletionService(db, new NullArtifactStore(), verbex, graph, blobs);
                            bool deleted = await cascade.DeleteSubjectCascadeAsync(context.TenantId, context.SubjectId, ct);
                            if (!deleted) throw new Exception("subject delete returned false");

                            // Postcondition: subject and every subordinate object were cascaded away.
                            if (graph.NodeCount != 0) throw new Exception("graph nodes not cascaded, remaining: " + graph.NodeCount);
                            if (graph.EdgeCount != 0) throw new Exception("graph edges not cascaded, remaining: " + graph.EdgeCount);
                            if (verbex.DocumentCount != 0) throw new Exception("indexed documents not cascaded, remaining: " + verbex.DocumentCount);
                            List<IngestionJobEvent> afterEvents = await db.IngestionJobEvents.EnumerateByJobAsync(context.TenantId, claimed.Id, ct);
                            if (afterEvents.Count != 0) throw new Exception("job events not cascaded, remaining: " + afterEvents.Count);
                            if (await db.IngestionJobs.ReadAsync(context.TenantId, claimed.Id, ct) != null) throw new Exception("ingestion job not deleted");
                            if (await db.SubjectLinks.ReadAsync(context.TenantId, context.LinkId, ct) != null) throw new Exception("link not deleted");
                            if (await db.Subjects.ReadAsync(context.TenantId, context.SubjectId, ct) != null) throw new Exception("subject not deleted");
                        })
                });
        }

        private static IngestionProcessor BuildProcessor(DatabaseDriverBase db, FakeDocumentAtomClient docAtom, FakeVerbexClient verbex, FakeLiteGraphClient graph)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
            Aes256Cipher cipher = new Aes256Cipher("test-signing-key");
            Pneuma.Server.Settings.IngestionSettings settings = new Pneuma.Server.Settings.IngestionSettings();
            Pneuma.Server.Settings.TelemetrySettings telemetrySettings = new Pneuma.Server.Settings.TelemetrySettings { Enabled = false };
            Pneuma.Server.Services.TelemetryService telemetry = new Pneuma.Server.Services.TelemetryService(telemetrySettings, logging);
            return new IngestionProcessor(db, docAtom, new FakePartioClient(), verbex, graph, new FakeVectorRepository(), blobs, new NullArtifactStore(), new FakeContentFetcher(), cipher, settings, new Pneuma.Server.Settings.RetrievalSettings(), logging, telemetry);
        }

        private static async Task<Context> SeedJobAsync(DatabaseDriverBase db, System.Threading.CancellationToken ct)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "IngestTenant" }, ct);
            Subject subject = await db.Subjects.CreateAsync(new Subject { TenantId = tenant.Id, DisplayName = "Chuck D" }, ct);
            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = tenant.Id, SubjectId = subject.Id, Url = "https://example.com/artifact" }, ct);
            await db.IngestionJobs.CreateAsync(new IngestionJob
            {
                TenantId = tenant.Id, SubjectId = subject.Id, LinkId = link.Id, SourceUrl = link.Url, Status = IngestionStatusEnum.Queued
            }, ct);
            return new Context { TenantId = tenant.Id, SubjectId = subject.Id, LinkId = link.Id };
        }

        private sealed class Context
        {
            public string TenantId { get; set; } = String.Empty;
            public string SubjectId { get; set; } = String.Empty;
            public string LinkId { get; set; } = String.Empty;
        }
    }
}
