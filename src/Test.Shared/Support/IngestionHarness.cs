namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
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

    /// <summary>
    /// A tenant, subject, collection, and link backed by a test database and fake RecallDB and LiteGraph stores, with
    /// helpers that queue and run ingestion jobs through the real pipeline.
    /// </summary>
    public sealed class IngestionHarness : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>The test database.</summary>
        public DatabaseDriverBase Db { get; private set; } = null!;

        /// <summary>The fake RecallDB store.</summary>
        public FakeRecallDbClient Recall { get; } = new FakeRecallDbClient();

        /// <summary>The fake LiteGraph store.</summary>
        public FakeLiteGraphClient Graph { get; } = new FakeLiteGraphClient();

        /// <summary>The seeded tenant id.</summary>
        public string TenantId { get; private set; } = String.Empty;

        /// <summary>The seeded subject id.</summary>
        public string SubjectId { get; private set; } = String.Empty;

        /// <summary>The seeded link id.</summary>
        public string LinkId { get; private set; } = String.Empty;

        /// <summary>The seeded collection id.</summary>
        public string CollectionId { get; private set; } = String.Empty;

        /// <summary>The blob store shared by every job the harness runs (pushed content lives here).</summary>
        public IBlobStore Blobs { get; } = new DiskBlobStore(Path.Combine(Path.GetTempPath(), "pneuma-test-blobs", Guid.NewGuid().ToString("N")));

        /// <summary>Embedding runner id stamped on jobs this harness queues, or null.</summary>
        public string? EmbeddingEndpointId { get; set; } = null;

        /// <summary>Opens crawled links' content in pipelines the harness builds, or null.</summary>
        public ICrawlContentSource? CrawlSource { get; set; } = null;

        #endregion

        #region Private-Members

        private string? _CompletionEndpointId = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Seed a tenant, subject, collection, and link; with a chat URL, also a completion runner pointing at it.</summary>
        /// <param name="chatBaseUrl">Base URL of a stub chat endpoint, or null.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The harness.</returns>
        public static async Task<IngestionHarness> CreateAsync(string? chatBaseUrl, CancellationToken ct)
        {
            IngestionHarness h = new IngestionHarness();
            h.Db = await TestDatabase.CreateAsync(ct).ConfigureAwait(false);
            Tenant tenant = await h.Db.Tenants.CreateAsync(new Tenant { Name = "HarnessTenant" }, ct).ConfigureAwait(false);
            h.TenantId = tenant.Id;
            Subject subject = await h.Db.Subjects.CreateAsync(new Subject { TenantId = tenant.Id, DisplayName = "Example Subject" }, ct).ConfigureAwait(false);
            h.SubjectId = subject.Id;
            SubjectLink link = await h.Db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = tenant.Id, SubjectId = subject.Id, Url = "https://example.com/doc" }, ct).ConfigureAwait(false);
            h.LinkId = link.Id;
            await h.Recall.EnsureTenantAsync(tenant.Id, tenant.Name, ct).ConfigureAwait(false);
            RecallCollection collection = await h.Recall.CreateCollectionAsync(tenant.Id, new RecallCollection { Name = "test", Dimensionality = 8 }, ct).ConfigureAwait(false);
            h.CollectionId = collection.Id;

            if (chatBaseUrl != null)
            {
                ModelRunner runner = await h.Db.ModelRunners.CreateAsync(new ModelRunner
                {
                    Name = "stub-chat-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                    Provider = ModelRunnerProviderEnum.Ollama,
                    BaseUrl = chatBaseUrl,
                    ApiType = "Ollama",
                    Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                    DefaultModel = "stub",
                    Active = true,
                    HealthCheckEnabled = false
                }, ct).ConfigureAwait(false);
                h._CompletionEndpointId = runner.Id;
            }

            return h;
        }

        #endregion

        #region Public-Methods

        /// <summary>Queue and run a job for the seeded link.</summary>
        /// <param name="fetcher">Content fetcher.</param>
        /// <param name="processor">Semantic processor.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The job id.</returns>
        public Task<string> IngestAsync(IContentFetcher fetcher, ISemanticProcessor processor, CancellationToken ct)
        {
            return RunJobAsync(LinkId, fetcher, processor, ct);
        }

        /// <summary>Create a second link on the subject and run a job for it.</summary>
        /// <param name="fetcher">Content fetcher.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The job id.</returns>
        public async Task<string> IngestOtherLinkAsync(IContentFetcher fetcher, CancellationToken ct)
        {
            SubjectLink other = await Db.SubjectLinks.CreateAsync(new SubjectLink { TenantId = TenantId, SubjectId = SubjectId, Url = "https://example.com/other" }, ct).ConfigureAwait(false);
            return await RunJobAsync(other.Id, fetcher, new FakeSemanticProcessor(), ct).ConfigureAwait(false);
        }

        /// <summary>Claim the next queued job (one queued by something else, such as the content service) and run it.</summary>
        /// <param name="processor">Semantic processor.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="atom">DocumentAtom fake; null uses a Text detector.</param>
        /// <returns>The job id.</returns>
        public async Task<string> RunQueuedAsync(ISemanticProcessor processor, CancellationToken ct, FakeDocumentAtomClient? atom = null)
        {
            IngestionProcessor pipeline = BuildPipeline(new FakeContentFetcher(), processor, atom);
            IngestionJob claimed = await Db.IngestionJobs.ClaimNextQueuedAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("no job claimed");
            await pipeline.ProcessAsync(claimed, ct).ConfigureAwait(false);
            return claimed.Id;
        }

        /// <summary>Claim and run every queued job until none is left.</summary>
        /// <param name="processor">Semantic processor.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>How many jobs ran.</returns>
        public async Task<int> RunAllQueuedAsync(ISemanticProcessor processor, CancellationToken ct)
        {
            IngestionProcessor pipeline = BuildPipeline(new FakeContentFetcher(), processor, null);
            int ran = 0;
            while (true)
            {
                IngestionJob? claimed = await Db.IngestionJobs.ClaimNextQueuedAsync(ct).ConfigureAwait(false);
                if (claimed == null) return ran;
                await pipeline.ProcessAsync(claimed, ct).ConfigureAwait(false);
                ran++;
            }
        }

        /// <summary>Chunks indexed by a job.</summary>
        /// <param name="jobId">Job id.</param>
        /// <returns>The count.</returns>
        public int ChunkCount(string jobId)
        {
            return Recall.AllDocumentTags().Count(t => t.TryGetValue("jobId", out string? j) && j == jobId);
        }

        /// <summary>Source and Cell nodes asserted by a job.</summary>
        /// <param name="jobId">Job id.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The count.</returns>
        public async Task<int> NodeCountAsync(string jobId, CancellationToken ct)
        {
            List<GraphNode> nodes = await Graph.SearchNodesByTagsAsync(new Dictionary<string, string> { { Ontology.TagAssertedByJob, jobId } }, 1000, ct).ConfigureAwait(false);
            return nodes.Count(n => n.NodeType == Ontology.NodeSource || n.NodeType == Ontology.NodeCell);
        }

        /// <summary>Read the seeded link.</summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The link.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the link no longer exists.</exception>
        public async Task<SubjectLink> ReadLinkAsync(CancellationToken ct)
        {
            return await Db.SubjectLinks.ReadAsync(TenantId, LinkId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("link gone");
        }

        /// <summary>Dispose the database.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync().ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<string> RunJobAsync(string linkId, IContentFetcher fetcher, ISemanticProcessor processor, CancellationToken ct)
        {
            IngestionJob job = await Db.IngestionJobs.CreateAsync(new IngestionJob
            {
                TenantId = TenantId,
                SubjectId = SubjectId,
                LinkId = linkId,
                SourceUrl = "https://example.com/doc",
                Status = IngestionStatusEnum.Queued,
                CollectionId = CollectionId,
                CompletionEndpointId = _CompletionEndpointId,
                EmbeddingEndpointId = EmbeddingEndpointId
            }, ct).ConfigureAwait(false);

            IngestionProcessor pipeline = BuildPipeline(fetcher, processor, null);
            IngestionJob claimed = await Db.IngestionJobs.ClaimNextQueuedAsync(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("no job claimed");
            if (claimed.Id != job.Id) throw new InvalidOperationException("claimed an unexpected job");
            await pipeline.ProcessAsync(claimed, ct).ConfigureAwait(false);
            return job.Id;
        }

        private IngestionProcessor BuildPipeline(IContentFetcher fetcher, ISemanticProcessor processor, FakeDocumentAtomClient? atom)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            TelemetryService telemetry = new TelemetryService(new TelemetrySettings { Enabled = false }, logging);
            IngestionProcessor pipeline = new IngestionProcessor(Db, atom ?? new FakeDocumentAtomClient("Text"), processor, new FakeGraphRepositoryFactory(Graph), Recall, Blobs, new NullArtifactStore(),
                fetcher, new Aes256Cipher("test-signing-key"), new IngestionSettings { RetryBackoffBaseMs = 0, MaxAttempts = 3 }, new ConcurrencyManager(new IngestionTuning()), logging, telemetry);
            pipeline.Resolver.CrawlSource = CrawlSource;
            return pipeline;
        }

        #endregion
    }
}
