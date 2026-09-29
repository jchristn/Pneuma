namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Ingestion.Refresh;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using SyslogLogging;

    /// <summary>A refresh service over an ingestion harness and a stub site that allows loopback.</summary>
    public sealed class LinkRefreshRig : IAsyncDisposable
    {
        public IngestionHarness H { get; private set; } = null!;

        public StubWebSite Site { get; } = new StubWebSite();

        public LinkRefreshService Service { get; private set; } = null!;

        private CrawlHttpClient _Http = null!;

        public static async Task<LinkRefreshRig> CreateAsync(CancellationToken ct)
        {
            LinkRefreshRig rig = new LinkRefreshRig();
            rig.H = await IngestionHarness.CreateAsync(null, ct);
            Subject subject = await rig.H.Db.Subjects.ReadAsync(rig.H.TenantId, rig.H.SubjectId, ct) ?? throw new Exception("subject gone");
            subject.Collection = rig.H.CollectionId;
            subject.EmbeddingModel = "stub-embedding";
            subject.InferenceModel = "stub-inference";
            await rig.H.Db.Subjects.UpdateAsync(subject, ct);
            FetchSafetySettings safety = new FetchSafetySettings();
            safety.AllowedPrivateHosts = new List<string> { "127.0.0.1" };
            rig._Http = new CrawlHttpClient(new FetchSafetyPolicy(safety));
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            rig.Service = new LinkRefreshService(rig.H.Db, rig._Http, new LinkRefreshSettings(), logging);
            return rig;
        }

        public async Task<SubjectLink> LinkAsync(string path, int interval, CancellationToken ct, Action<SubjectLink>? configure = null)
        {
            SubjectLink link = new SubjectLink
            {
                TenantId = H.TenantId,
                SubjectId = H.SubjectId,
                Url = Site.Url(path),
                Status = SubjectLinkStatusEnum.Ingested,
                RefreshIntervalMinutes = interval,
                NextRefreshUtc = DateTime.UtcNow.AddMinutes(-5)
            };
            configure?.Invoke(link);
            return await H.Db.SubjectLinks.CreateAsync(link, ct);
        }

        public async Task<SubjectLink> ReadAsync(string id, CancellationToken ct)
        {
            return await H.Db.SubjectLinks.ReadAsync(H.TenantId, id, ct) ?? throw new Exception("link gone");
        }

        public async ValueTask DisposeAsync()
        {
            _Http.Dispose();
            Site.Dispose();
            await H.DisposeAsync();
        }
    }
}
