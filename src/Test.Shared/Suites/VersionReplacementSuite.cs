namespace Test.Shared.Suites
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using Pneuma.Core.Storage;
    using SyslogLogging;
    using Test.Shared.Support;
    using Touchstone.Core;

    /// <summary>
    /// One live version per link: a re-ingest removes the earlier version's chunks and Source and Cell nodes once the
    /// new version is indexed, keeps shared entity nodes, leaves the old version in place when the re-ingest fails, and
    /// a retry clears its failed attempt's partial output so nothing is duplicated.
    /// </summary>
    public static class VersionReplacementSuite
    {
        private const string _SubgraphJson = "{\"nodes\":[{\"ref\":\"n1\",\"nodeType\":\"Organization\",\"name\":\"Example Subject\",\"canonicalName\":\"example subject\",\"content\":\"An organization.\",\"confidence\":0.9}],\"edges\":[]}";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "VersionReplacement",
                displayName: "Version replacement on re-ingest (one live version per link)",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("VersionReplacement", "Reingest_LeavesOnlyNewVersion", "Re-ingesting changed content leaves only the new job's chunks and Source and Cell nodes, and moves CurrentJobId",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string job1 = await h.IngestAsync(new FakeContentFetcher("First version. Example Subject was founded in 2010."), new FakeSemanticProcessor(), ct);
                                string job2 = await h.IngestAsync(new FakeContentFetcher("Second version. Example Subject moved to a new city in 2020."), new FakeSemanticProcessor(), ct);

                                if (h.ChunkCount(job1) != 0) throw new Exception("the first version's chunks should be gone, found " + h.ChunkCount(job1));
                                if (h.ChunkCount(job2) == 0) throw new Exception("the second version's chunks should be indexed");
                                if (await h.NodeCountAsync(job1, ct) != 0) throw new Exception("the first version's Source and Cell nodes should be gone");
                                if (await h.NodeCountAsync(job2, ct) == 0) throw new Exception("the second version's nodes should exist");

                                SubjectLink link = await h.ReadLinkAsync(ct);
                                if (link.CurrentJobId != job2) throw new Exception("CurrentJobId should be the second job, got " + link.CurrentJobId);
                                List<IngestionJobEvent> events = await h.Db.IngestionJobEvents.EnumerateByJobAsync(h.TenantId, job2, ct);
                                if (!events.Any(e => (e.Message ?? String.Empty).Contains("Replaced the previous version"))) throw new Exception("the job log should say the previous version was replaced");
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "Reingest_KeepsSharedEntities", "Retiring an earlier version keeps the entity nodes it created, which the new version reuses",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.ChatText = _SubgraphJson;
                                await using (IngestionHarness h = await IngestionHarness.CreateAsync(stub.BaseUrl, ct))
                                {
                                    await h.IngestAsync(new FakeContentFetcher("Example Subject, version one."), new FakeSemanticProcessor(), ct);
                                    List<GraphNode> before = await h.Graph.SearchNodesByTagsAsync(new Dictionary<string, string> { { Ontology.TagNodeType, "Organization" } }, 100, ct);
                                    if (before.Count != 1) throw new Exception("expected one entity after the first ingest, got " + before.Count);

                                    await h.IngestAsync(new FakeContentFetcher("Example Subject, version two."), new FakeSemanticProcessor(), ct);
                                    List<GraphNode> after = await h.Graph.SearchNodesByTagsAsync(new Dictionary<string, string> { { Ontology.TagNodeType, "Organization" } }, 100, ct);
                                    if (after.Count != 1 || after[0].Id != before[0].Id) throw new Exception("the shared entity node must survive retirement and be reused");
                                }
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "Unchanged_KeepsCurrentVersion", "A re-ingest of unchanged content completes early and leaves the current version and CurrentJobId alone",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string job1 = await h.IngestAsync(new FakeContentFetcher("Same content."), new FakeSemanticProcessor(), ct);
                                string job2 = await h.IngestAsync(new FakeContentFetcher("Same content."), new FakeSemanticProcessor(), ct);
                                if (h.ChunkCount(job1) == 0) throw new Exception("an early-completed re-ingest must not remove the live version");
                                if (h.ChunkCount(job2) != 0) throw new Exception("an early-completed job writes nothing");
                                if ((await h.ReadLinkAsync(ct)).CurrentJobId != job1) throw new Exception("CurrentJobId should stay on the job that holds the data");
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "FailedReingest_KeepsOldVersion", "A re-ingest that fails leaves the old version searchable and CurrentJobId unchanged",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string job1 = await h.IngestAsync(new FakeContentFetcher("First version."), new FakeSemanticProcessor(), ct);
                                string job2 = await h.IngestAsync(new ThrowingContentFetcher(() => new HttpRequestException("Not Found", null, HttpStatusCode.NotFound)), new FakeSemanticProcessor(), ct);
                                IngestionJob failed = await h.Db.IngestionJobs.ReadAsync(h.TenantId, job2, ct) ?? throw new Exception("job gone");
                                if (failed.Status != IngestionStatusEnum.Failed) throw new Exception("the re-ingest should fail");
                                if (h.ChunkCount(job1) == 0) throw new Exception("the old version must stay searchable after a failed re-ingest");
                                if ((await h.ReadLinkAsync(ct)).CurrentJobId != job1) throw new Exception("CurrentJobId must not move on failure");
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "Retry_ClearsFailedAttempt", "A retry after an Embedding failure leaves exactly one set of Source and Cell nodes and chunks",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string job = await h.IngestAsync(new FakeContentFetcher("Retry me."), new FlakyEmbeddingSemanticProcessor(1), ct);
                                IngestionJob done = await h.Db.IngestionJobs.ReadAsync(h.TenantId, job, ct) ?? throw new Exception("job gone");
                                if (done.Status != IngestionStatusEnum.Completed) throw new Exception("the retry should complete, got " + done.Status + " (" + done.Error + ")");
                                List<IngestionJobAttempt> attempts = await h.Db.IngestionJobAttempts.EnumerateByJobAsync(h.TenantId, job, ct);
                                if (attempts.Count != 2) throw new Exception("expected 2 attempts, got " + attempts.Count);
                                List<GraphNode> sources = await h.Graph.SearchNodesByTagsAsync(new Dictionary<string, string> { { Ontology.TagAssertedByJob, job }, { Ontology.TagNodeType, Ontology.NodeSource } }, 100, ct);
                                if (sources.Count != 1) throw new Exception("expected one Source node after the retry, got " + sources.Count);
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "RemovalFailure_WarnsAndKeepsBoth", "When the old chunks cannot be removed, the job completes with a warning and the next successful ingest cleans up",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string job1 = await h.IngestAsync(new FakeContentFetcher("Version one."), new FakeSemanticProcessor(), ct);
                                h.Recall.FailDeletes = true;
                                string job2 = await h.IngestAsync(new FakeContentFetcher("Version two."), new FakeSemanticProcessor(), ct);
                                IngestionJob second = await h.Db.IngestionJobs.ReadAsync(h.TenantId, job2, ct) ?? throw new Exception("job gone");
                                if (second.Status != IngestionStatusEnum.Completed) throw new Exception("a removal failure must not fail the job");
                                if (!second.Warnings.Any(w => w.Contains("previous version"))) throw new Exception("expected a warning about the previous version");
                                if (h.ChunkCount(job1) == 0) throw new Exception("the old chunks stay while removal fails");

                                h.Recall.FailDeletes = false;
                                string job3 = await h.IngestAsync(new FakeContentFetcher("Version three."), new FakeSemanticProcessor(), ct);
                                if (h.ChunkCount(job1) != 0 || h.ChunkCount(job2) != 0) throw new Exception("the next successful ingest should remove both earlier versions");
                                if (h.ChunkCount(job3) == 0) throw new Exception("the newest version should be indexed");
                            }
                        }),

                    new TestCaseDescriptor("VersionReplacement", "OtherLinks_AreUntouched", "Re-ingesting one link never removes another link's chunks, even with identical content",
                        executeAsync: async ct =>
                        {
                            await using (IngestionHarness h = await IngestionHarness.CreateAsync(null, ct))
                            {
                                string otherLinkJob = await h.IngestOtherLinkAsync(new FakeContentFetcher("Shared text."), ct);
                                await h.IngestAsync(new FakeContentFetcher("Shared text."), new FakeSemanticProcessor(), ct);
                                await h.IngestAsync(new FakeContentFetcher("Changed text."), new FakeSemanticProcessor(), ct);
                                if (h.ChunkCount(otherLinkJob) == 0) throw new Exception("another link's chunks must be untouched");
                            }
                        })
                });
        }
    }
}
