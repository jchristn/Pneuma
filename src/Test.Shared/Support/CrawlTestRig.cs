namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using SyslogLogging;

    /// <summary>
    /// The crawl framework over an <see cref="IngestionHarness"/> database, with a <see cref="FakeCrawler"/> registered
    /// for Web plans. Runs operations end to end: start, enumerate and dispatch, ingest the queued jobs, and finish.
    /// </summary>
    public sealed class CrawlTestRig : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>The ingestion harness (database, fakes, and a configured subject).</summary>
        public IngestionHarness H { get; private set; } = null!;

        /// <summary>The fake source.</summary>
        public FakeCrawler Crawler { get; } = new FakeCrawler(CrawlPlanTypeEnum.Web);

        /// <summary>Registered crawlers.</summary>
        public CrawlerFactory Crawlers { get; } = new CrawlerFactory();

        /// <summary>Cipher for plan secrets.</summary>
        public Aes256Cipher Cipher { get; } = new Aes256Cipher("crawl-test-signing-key");

        /// <summary>Plan service.</summary>
        public CrawlPlanService Plans { get; private set; } = null!;

        /// <summary>Sync service.</summary>
        public CrawlSyncService Sync { get; private set; } = null!;

        /// <summary>Scheduler.</summary>
        public CrawlSchedulerService Scheduler { get; private set; } = null!;

        /// <summary>Crawling settings the scheduler uses.</summary>
        public CrawlingSettings Settings { get; } = new CrawlingSettings { SchedulerEnabled = false };

        /// <summary>Logging module.</summary>
        public LoggingModule Logging { get; } = new LoggingModule();

        #endregion

        #region Constructors-and-Factories

        /// <summary>Create a rig with a configured subject.</summary>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The rig.</returns>
        public static async Task<CrawlTestRig> CreateAsync(CancellationToken ct)
        {
            CrawlTestRig rig = new CrawlTestRig();
            rig.Logging.Settings.EnableConsole = false;
            rig.H = await IngestionHarness.CreateAsync(null, ct).ConfigureAwait(false);
            Subject subject = await rig.H.Db.Subjects.ReadAsync(rig.H.TenantId, rig.H.SubjectId, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("subject gone");
            subject.Collection = rig.H.CollectionId;
            subject.EmbeddingModel = "stub-embedding";
            subject.InferenceModel = "stub-inference";
            await rig.H.Db.Subjects.UpdateAsync(subject, ct).ConfigureAwait(false);

            rig.Crawlers.Register(rig.Crawler);
            rig.Plans = new CrawlPlanService(rig.H.Db, rig.Cipher, rig.Crawlers);
            rig.Sync = new CrawlSyncService(rig.H.Db, rig.Crawlers, rig.Logging);
            rig.Scheduler = rig.NewScheduler();
            rig.H.CrawlSource = new CrawlContentSource(rig.H.Db, rig.Plans, rig.Crawlers);
            return rig;
        }

        #endregion

        #region Public-Methods

        /// <summary>A second scheduler over the same database (another server).</summary>
        /// <returns>The scheduler.</returns>
        public CrawlSchedulerService NewScheduler()
        {
            return new CrawlSchedulerService(H.Db, Plans, Sync, Settings, Logging);
        }

        /// <summary>Create a Web plan on the harness subject.</summary>
        /// <param name="configure">Changes to the plan before it is saved, or null.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The stored plan.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the plan is invalid.</exception>
        public async Task<CrawlPlan> CreatePlanAsync(Action<CrawlPlan>? configure, CancellationToken ct)
        {
            CrawlPlan plan = new CrawlPlan
            {
                TenantId = H.TenantId,
                SubjectId = H.SubjectId,
                Name = "Fake site",
                Type = CrawlPlanTypeEnum.Web,
                Web = new WebCrawlSettings { StartUrls = new List<string> { "https://fake.example/" } }
            };
            configure?.Invoke(plan);
            List<string> errors = Plans.Validate(plan, null);
            if (errors.Count > 0) throw new InvalidOperationException("invalid plan: " + String.Join(" ", errors));
            CrawlPlanSaveResult saved = await Plans.CreateAsync(plan, ct).ConfigureAwait(false);
            return saved.Plan;
        }

        /// <summary>
        /// Run one operation to its end: start it, wait for enumeration and dispatch, ingest every queued job, and run a
        /// scheduler pass to finish it.
        /// </summary>
        /// <param name="planId">Plan identifier.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>The finished operation.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the operation cannot start.</exception>
        public async Task<CrawlOperation> RunToEndAsync(string planId, CancellationToken ct)
        {
            CrawlStartResult started = await Scheduler.StartAsync(H.TenantId, planId, CrawlTriggerEnum.Manual, ct).ConfigureAwait(false);
            if (started.StatusCode != 202 || started.Operation == null) throw new InvalidOperationException("start failed: " + started.StatusCode + " " + started.Error);
            await Scheduler.WaitAsync(H.TenantId, planId).ConfigureAwait(false);
            await H.RunAllQueuedAsync(new FakeSemanticProcessor(), ct).ConfigureAwait(false);
            await Scheduler.RunPassAsync(ct).ConfigureAwait(false);
            return await H.Db.CrawlOperations.ReadAsync(H.TenantId, started.Operation.Id, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("operation gone");
        }

        /// <summary>Dispose the scheduler and the harness.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            Scheduler?.Dispose();
            if (H != null) await H.DisposeAsync().ConfigureAwait(false);
        }

        #endregion
    }
}
