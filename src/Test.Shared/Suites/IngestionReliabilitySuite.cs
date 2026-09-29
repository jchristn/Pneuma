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
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Enums;
    using Pneuma.Core.Ingestion.Models;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Stages;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
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
    /// Ingestion reliability: failure categories and their retry policy, attempt history, completeness counters, and
    /// warnings for work a job drops (so a partial ingest is never reported as complete).
    /// </summary>
    public static class IngestionReliabilitySuite
    {
        private const string _SubgraphJson = "{\"nodes\":[{\"ref\":\"n1\",\"nodeType\":\"Organization\",\"name\":\"Example Subject\",\"canonicalName\":\"example subject\",\"content\":\"An organization.\",\"confidence\":0.9}],\"edges\":[]}";

        /// <summary>Build the suite.</summary>
        /// <returns>Suite descriptor.</returns>
        public static TestSuiteDescriptor Build()
        {
            return new TestSuiteDescriptor(
                suiteId: "IngestionReliability",
                displayName: "Ingestion reliability (failure categories, attempts, completeness)",
                cases: new List<TestCaseDescriptor>
                {
                    new TestCaseDescriptor("IngestionReliability", "Classifier_MapsFailuresToCategories", "Each failure maps to its category and retry policy",
                        executeAsync: ct =>
                        {
                            Expect(new IngestionHardFailException(IngestionStageEnum.TypeDetection, IngestionFailureCategoryEnum.UnsupportedType, "x"), IngestionStageEnum.TypeDetection, IngestionFailureCategoryEnum.UnsupportedType, false);
                            Expect(new FetchBlockedException("http://127.0.0.1/", "private-address", "blocked"), IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.Blocked, false);
                            Expect(new ContentTooLargeException(10, "too large"), IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.TooLarge, false);
                            Expect(new ModelEndpointUnavailableException(429, "busy"), IngestionStageEnum.Embedding, IngestionFailureCategoryEnum.ModelUnavailable, true);
                            Expect(new ModelRequestRejectedException(400, true, "too long"), IngestionStageEnum.Embedding, IngestionFailureCategoryEnum.ModelRejected, false);
                            Expect(new HttpRequestException("gone", null, HttpStatusCode.NotFound), IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.Fetch, false);
                            Expect(new HttpRequestException("busy", null, HttpStatusCode.ServiceUnavailable), IngestionStageEnum.ContentRetrieval, IngestionFailureCategoryEnum.Fetch, true);
                            Expect(new IntegrationClientException("recalldb", "/documents", 500, "down"), IngestionStageEnum.Indexing, IngestionFailureCategoryEnum.Storage, true);
                            Expect(new IntegrationClientException("documentatom", "/atom", 500, "down"), IngestionStageEnum.CellExtraction, IngestionFailureCategoryEnum.Extraction, true);
                            Expect(new TimeoutException("slow"), IngestionStageEnum.Embedding, IngestionFailureCategoryEnum.Timeout, true);
                            Expect(new NotSupportedException("no route"), IngestionStageEnum.CellExtraction, IngestionFailureCategoryEnum.UnsupportedType, false);
                            Expect(new PartialLossException(new List<string> { "w" }), IngestionStageEnum.Done, IngestionFailureCategoryEnum.PartialLoss, true);
                            Expect(new InvalidOperationException("boom"), IngestionStageEnum.Hydration, IngestionFailureCategoryEnum.Internal, true);
                            if (IngestionFailureClassifier.Classify(new ModelEndpointUnavailableException(503, "x"), IngestionStageEnum.Embedding).BackoffMultiplier <= 1)
                                throw new Exception("a rate-limited endpoint should back off longer than a normal failure");
                            if (String.IsNullOrEmpty(IngestionFailureClassifier.Describe(IngestionFailureCategoryEnum.Storage).Remediation))
                                throw new Exception("every category needs remediation text");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("IngestionReliability", "ModelResponseErrors_MapErrorText", "Unsuccessful model responses become typed exceptions",
                        executeAsync: ct =>
                        {
                            if (!(ModelResponseErrors.ToException("embedding", "HTTP 429 Too Many Requests") is ModelEndpointUnavailableException)) throw new Exception("429 should be unavailable");
                            if (!(ModelResponseErrors.ToException("embedding", "status 503: at capacity") is ModelEndpointUnavailableException)) throw new Exception("503 should be unavailable");
                            Exception rejected = ModelResponseErrors.ToException("embedding", "HTTP 400: the input length exceeds the context length");
                            if (!(rejected is ModelRequestRejectedException r1) || !r1.IsContextLength) throw new Exception("context-length 400 should be a context-length rejection");
                            if (!(ModelResponseErrors.ToException("chat", "HTTP 401 unauthorized") is ModelRequestRejectedException r2) || r2.IsContextLength) throw new Exception("401 should be a plain rejection");
                            if (!(ModelResponseErrors.ToException("chat", null) is InvalidOperationException)) throw new Exception("no error text should be an invalid operation");
                            return Task.CompletedTask;
                        }),

                    new TestCaseDescriptor("IngestionReliability", "CleanRun_RecordsCompletenessAndOneAttempt", "A clean run completes with matching counts, no warnings, and one successful attempt",
                        executeAsync: async ct =>
                        {
                            using (StubModelServer stub = new StubModelServer())
                            {
                                stub.ChatText = _SubgraphJson;
                                await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                                {
                                    FakeRecallDbClient recall = new FakeRecallDbClient();
                                    ModelRunner runner = await CreateChatRunnerAsync(db, stub.BaseUrl, ct);
                                    SeededJob seeded = await SeedJobAsync(db, recall, runner.Id, ct);
                                    IngestionProcessor processor = BuildProcessor(db, recall, new FakeContentFetcher(), new FakeSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0 });
                                    await RunAsync(db, processor, ct);

                                    IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                    if (job.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + job.Status + " (" + job.Error + ")");
                                    if (job.Warnings.Count != 0) throw new Exception("a clean run should have no warnings: " + String.Join("; ", job.Warnings));
                                    IngestionCompleteness c = job.Completeness;
                                    if (c.CellsExtracted < 1 || c.ClassificationBatches != 1 || c.ClassificationBatchesFailed != 0) throw new Exception("unexpected classification counts");
                                    if (c.CellNodesCreated != c.CellsExtracted || c.CellNodesFailed != 0) throw new Exception("every cell should have a node");
                                    if (c.ChunksProduced < 1 || c.ChunksEmbedded != c.ChunksProduced || c.ChunksIndexed != c.ChunksProduced) throw new Exception("chunk counts should match: " + c.ChunksProduced + "/" + c.ChunksEmbedded + "/" + c.ChunksIndexed);
                                    if (job.FailureCategory != null) throw new Exception("a completed job has no failure category");

                                    List<IngestionJobAttempt> attempts = await db.IngestionJobAttempts.EnumerateByJobAsync(seeded.TenantId, seeded.JobId, ct);
                                    if (attempts.Count != 1 || !attempts[0].Succeeded) throw new Exception("expected one successful attempt, got " + attempts.Count);

                                    SubjectLink link = await db.SubjectLinks.ReadAsync(seeded.TenantId, seeded.LinkId, ct) ?? throw new Exception("link gone");
                                    if (link.WarningCount != 0 || link.FailureCategory != null) throw new Exception("a clean link has no warnings or failure category");
                                }
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "ClassificationFailure_CompletesWithWarning", "A failed classification still ingests the text but records a warning on the job and link",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                IngestionProcessor processor = BuildProcessor(db, recall, new FakeContentFetcher(), new FakeSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0 });
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.Status != IngestionStatusEnum.Completed) throw new Exception("expected Completed, got " + job.Status + " (" + job.Error + ")");
                                if (job.Warnings.Count != 1 || !job.Warnings[0].Contains("Classification")) throw new Exception("expected one classification warning, got: " + String.Join("; ", job.Warnings));
                                if (job.Completeness.ClassificationBatchesFailed != 1) throw new Exception("the failed batch should be counted");
                                if (recall.DocumentCount < 1) throw new Exception("the text should still be indexed");

                                SubjectLink link = await db.SubjectLinks.ReadAsync(seeded.TenantId, seeded.LinkId, ct) ?? throw new Exception("link gone");
                                if (link.Status != SubjectLinkStatusEnum.Ingested || link.WarningCount != 1) throw new Exception("the link should be Ingested with one warning");

                                List<IngestionJobEvent> events = await db.IngestionJobEvents.EnumerateByJobAsync(seeded.TenantId, seeded.JobId, ct);
                                if (!events.Any(e => e.Stage == IngestionStageEnum.Done && (e.Message ?? String.Empty).Contains("warning"))) throw new Exception("the completion event should mention the warning");
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "PartialLossPolicyFail_FailsAndRetries", "Under the Fail policy, dropped work fails the job with category PartialLoss after retrying",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                IngestionSettings settings = new IngestionSettings { RetryBackoffBaseMs = 0, MaxAttempts = 2, PartialLossPolicy = PartialLossPolicyEnum.Fail };
                                IngestionProcessor processor = BuildProcessor(db, recall, new FakeContentFetcher(), new FakeSemanticProcessor(), settings);
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.Status != IngestionStatusEnum.Failed) throw new Exception("expected Failed, got " + job.Status);
                                if (job.FailureCategory != IngestionFailureCategoryEnum.PartialLoss) throw new Exception("expected PartialLoss, got " + job.FailureCategory);
                                List<IngestionJobAttempt> attempts = await db.IngestionJobAttempts.EnumerateByJobAsync(seeded.TenantId, seeded.JobId, ct);
                                if (attempts.Count != 2) throw new Exception("PartialLoss is retryable; expected 2 attempts, got " + attempts.Count);
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "UnsupportedType_FailsOnceWithCategory", "An unsupported type fails on the first attempt with category UnsupportedType on the job and link",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                IngestionProcessor processor = BuildProcessor(db, recall, new FakeContentFetcher(new byte[] { 0x00, 0x01, 0xFF, 0xFE, 0x89, 0x00 }), new FakeSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0 }, new FakeDocumentAtomClient("Unknown"));
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.Status != IngestionStatusEnum.Failed || job.FailureCategory != IngestionFailureCategoryEnum.UnsupportedType) throw new Exception("expected Failed/UnsupportedType, got " + job.Status + "/" + job.FailureCategory);
                                List<IngestionJobAttempt> attempts = await db.IngestionJobAttempts.EnumerateByJobAsync(seeded.TenantId, seeded.JobId, ct);
                                if (attempts.Count != 1) throw new Exception("a deterministic failure must not be retried; attempts " + attempts.Count);
                                if (attempts[0].Succeeded || attempts[0].FailureCategory != IngestionFailureCategoryEnum.UnsupportedType) throw new Exception("the attempt should record the category");

                                SubjectLink link = await db.SubjectLinks.ReadAsync(seeded.TenantId, seeded.LinkId, ct) ?? throw new Exception("link gone");
                                if (link.FailureCategory != IngestionFailureCategoryEnum.UnsupportedType) throw new Exception("the link should carry the category");
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "FetchNotFound_IsNotRetried", "A 404 from the source is category Fetch and is not retried",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                ThrowingContentFetcher fetcher = new ThrowingContentFetcher(() => new HttpRequestException("Not Found", null, HttpStatusCode.NotFound));
                                IngestionProcessor processor = BuildProcessor(db, recall, fetcher, new FakeSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0, MaxAttempts = 3 });
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.FailureCategory != IngestionFailureCategoryEnum.Fetch) throw new Exception("expected Fetch, got " + job.FailureCategory);
                                if (fetcher.Calls != 1) throw new Exception("a 404 must not be retried; fetches " + fetcher.Calls);
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "FetchUnavailable_RetriesToMaxAttempts", "A 503 from the source is retried up to MaxAttempts and each attempt is recorded",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                ThrowingContentFetcher fetcher = new ThrowingContentFetcher(() => new HttpRequestException("Service Unavailable", null, HttpStatusCode.ServiceUnavailable));
                                IngestionProcessor processor = BuildProcessor(db, recall, fetcher, new FakeSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0, MaxAttempts = 3 });
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.Status != IngestionStatusEnum.Failed || job.FailureCategory != IngestionFailureCategoryEnum.Fetch) throw new Exception("expected Failed/Fetch");
                                if (fetcher.Calls != 3) throw new Exception("expected 3 fetches, got " + fetcher.Calls);
                                List<IngestionJobAttempt> attempts = await db.IngestionJobAttempts.EnumerateByJobAsync(seeded.TenantId, seeded.JobId, ct);
                                if (attempts.Count != 3 || attempts.Any(a => a.Succeeded)) throw new Exception("expected 3 failed attempts, got " + attempts.Count);
                                if (attempts[0].AttemptNumber != 1 || attempts[2].AttemptNumber != 3) throw new Exception("attempts should be numbered in order");
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "MissingVectors_FailInsteadOfDropping", "A chunk without an embedding fails the job rather than being silently left out of the index",
                        executeAsync: async ct =>
                        {
                            await using (DatabaseDriverBase db = await TestDatabase.CreateAsync(ct))
                            {
                                FakeRecallDbClient recall = new FakeRecallDbClient();
                                SeededJob seeded = await SeedJobAsync(db, recall, null, ct);
                                IngestionProcessor processor = BuildProcessor(db, recall, new FakeContentFetcher(), new PartialEmbeddingSemanticProcessor(), new IngestionSettings { RetryBackoffBaseMs = 0, MaxAttempts = 1 });
                                await RunAsync(db, processor, ct);

                                IngestionJob job = await ReadJobAsync(db, seeded, ct);
                                if (job.Status != IngestionStatusEnum.Failed) throw new Exception("expected Failed, got " + job.Status);
                                if (job.Stage != IngestionStageEnum.Embedding) throw new Exception("expected failure at Embedding, got " + job.Stage);
                                if (recall.DocumentCount != 0) throw new Exception("nothing should be indexed when a vector is missing");
                            }
                        }),

                    new TestCaseDescriptor("IngestionReliability", "JobsRoute_FiltersByCategoryAndRejectsUnknown", "GET /v1.0/jobs filters by failureCategory and hasWarnings and rejects unknown values",
                        executeAsync: async ct =>
                        {
                            await using (TestServer server = await TestServer.CreateAsync(ct))
                            {
                                string token = await ApiClientHelper.LoginAsync(server.BaseUrl, "admin@pneuma", "password", ct);
                                ApiResult bad = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs?failureCategory=Bogus", token, null, ct);
                                if (bad.StatusCode != 400) throw new Exception("unknown category should be 400, got " + bad.StatusCode);
                                ApiResult badBool = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs?hasWarnings=maybe", token, null, ct);
                                if (badBool.StatusCode != 400) throw new Exception("invalid hasWarnings should be 400, got " + badBool.StatusCode);
                                ApiResult good = await ApiClientHelper.CallAsync(HttpMethod.Get, server.BaseUrl + "/v1.0/jobs?failureCategory=fetch&hasWarnings=false", token, null, ct);
                                if (good.StatusCode != 200) throw new Exception("valid filters should be 200, got " + good.StatusCode + ": " + good.Body);
                            }
                        })
                });
        }

        private static void Expect(Exception exception, IngestionStageEnum stage, IngestionFailureCategoryEnum category, bool retryable)
        {
            IngestionFailureClassification result = IngestionFailureClassifier.Classify(exception, stage);
            if (result.Category != category) throw new Exception(exception.GetType().Name + " at " + stage + ": expected " + category + ", got " + result.Category);
            if (result.Retryable != retryable) throw new Exception(exception.GetType().Name + " at " + stage + ": expected retryable=" + retryable);
        }

        private static IngestionProcessor BuildProcessor(DatabaseDriverBase db, FakeRecallDbClient recall, IContentFetcher fetcher, ISemanticProcessor processor, IngestionSettings settings, FakeDocumentAtomClient? atom = null)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            IBlobStore blobs = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));
            TelemetryService telemetry = new TelemetryService(new TelemetrySettings { Enabled = false }, logging);
            ConcurrencyManager concurrency = new ConcurrencyManager(new IngestionTuning());
            return new IngestionProcessor(db, atom ?? new FakeDocumentAtomClient("Text"), processor, new FakeGraphRepositoryFactory(new FakeLiteGraphClient()), recall, blobs, new NullArtifactStore(), fetcher, new Aes256Cipher("test-signing-key"), settings, concurrency, logging, telemetry);
        }

        private static async Task<ModelRunner> CreateChatRunnerAsync(DatabaseDriverBase db, string baseUrl, CancellationToken ct)
        {
            return await db.ModelRunners.CreateAsync(new ModelRunner
            {
                Name = "stub-chat-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Provider = ModelRunnerProviderEnum.Ollama,
                BaseUrl = baseUrl,
                ApiType = "Ollama",
                Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                DefaultModel = "stub",
                Active = true,
                HealthCheckEnabled = false
            }, ct);
        }

        private static async Task<SeededJob> SeedJobAsync(DatabaseDriverBase db, FakeRecallDbClient recall, string? completionEndpointId, CancellationToken ct)
        {
            Tenant tenant = await db.Tenants.CreateAsync(new Tenant { Name = "ReliabilityTenant" }, ct);
            Subject subject = await db.Subjects.CreateAsync(new Subject { TenantId = tenant.Id, DisplayName = "Example Subject" }, ct);
            SubjectLink link = await db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = tenant.Id, SubjectId = subject.Id, Url = "https://example.com/doc" }, ct);
            await recall.EnsureTenantAsync(tenant.Id, tenant.Name, ct);
            RecallCollection collection = await recall.CreateCollectionAsync(tenant.Id, new RecallCollection { Name = "test", Dimensionality = 8 }, ct);
            IngestionJob job = await db.IngestionJobs.CreateAsync(new IngestionJob
            {
                TenantId = tenant.Id,
                SubjectId = subject.Id,
                LinkId = link.Id,
                SourceUrl = link.Url,
                Status = IngestionStatusEnum.Queued,
                CollectionId = collection.Id,
                CompletionEndpointId = completionEndpointId
            }, ct);
            return new SeededJob { TenantId = tenant.Id, LinkId = link.Id, JobId = job.Id };
        }

        private static async Task RunAsync(DatabaseDriverBase db, IngestionProcessor processor, CancellationToken ct)
        {
            IngestionJob claimed = await db.IngestionJobs.ClaimNextQueuedAsync(ct) ?? throw new Exception("no job claimed");
            await processor.ProcessAsync(claimed, ct);
        }

        private static async Task<IngestionJob> ReadJobAsync(DatabaseDriverBase db, SeededJob seeded, CancellationToken ct)
        {
            return await db.IngestionJobs.ReadAsync(seeded.TenantId, seeded.JobId, ct) ?? throw new Exception("job gone");
        }
    }
}
