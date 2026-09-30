namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling.Crawlers;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Configuration;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Ontologies;
    using Pneuma.Core.Security;
    using Pneuma.Core.Wizard;
    using Pneuma.Server.Services;
    using SyslogLogging;

    /// <summary>
    /// The new subject wizard over an ingestion harness: a stub chat model, a stub web site the grounding fetch may reach
    /// (loopback allowed), the drafting service, and the commit service.
    /// </summary>
    public sealed class WizardRig : IAsyncDisposable
    {
        /// <summary>Harness (database, tenant, collection).</summary>
        public IngestionHarness H { get; private set; } = null!;

        /// <summary>Stub chat model.</summary>
        public StubModelServer Model { get; } = new StubModelServer();

        /// <summary>Stub web site for grounding URLs.</summary>
        public StubWebSite Site { get; } = new StubWebSite();

        /// <summary>The tenant's completion endpoint (pointing at <see cref="Model"/>).</summary>
        public ModelRunner Runner { get; private set; } = null!;

        /// <summary>Drafting service.</summary>
        public SubjectWizardService Service { get; private set; } = null!;

        /// <summary>Commit service.</summary>
        public SubjectWizardCommitService Commit { get; private set; } = null!;

        /// <summary>Wizard limits used by both services.</summary>
        public WizardSettings Settings { get; } = new WizardSettings();

        private CrawlHttpClient _Http = null!;

        /// <summary>Create the rig.</summary>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="allowLoopback">True to let grounding URLs reach 127.0.0.1.</param>
        /// <returns>The rig.</returns>
        public static async Task<WizardRig> CreateAsync(CancellationToken ct, bool allowLoopback = true)
        {
            WizardRig rig = new WizardRig();
            rig.H = await IngestionHarness.CreateAsync(null, ct);
            rig.Runner = await rig.H.Db.ModelRunners.CreateAsync(new ModelRunner
            {
                TenantId = rig.H.TenantId,
                Name = "stub-wizard-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Provider = ModelRunnerProviderEnum.Ollama,
                BaseUrl = rig.Model.BaseUrl,
                ApiType = "Ollama",
                Capabilities = new List<ModelCapabilityEnum> { ModelCapabilityEnum.Completion },
                DefaultModel = "stub",
                MaxRetries = 0,
                Active = true,
                HealthCheckEnabled = false
            }, ct);
            FetchSafetySettings safety = new FetchSafetySettings();
            if (allowLoopback) safety.AllowedPrivateHosts = new List<string> { "127.0.0.1" };
            rig._Http = new CrawlHttpClient(new FetchSafetyPolicy(safety));
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;
            rig.Service = new SubjectWizardService(rig.H.Db, new Aes256Cipher("test-signing-key"), rig._Http, rig.Settings, logging);
            rig.Commit = new SubjectWizardCommitService(rig.H.Db, new OntologyService(rig.H.Db), rig.H.Recall, rig.H.CollectionId, rig.Settings, logging);
            return rig;
        }

        /// <summary>A request with the given description, drafted with the rig's runner.</summary>
        /// <param name="description">Subject description.</param>
        /// <returns>The request.</returns>
        public WizardGenerateRequest Request(string description = "Charlie Parker, the bebop saxophonist. For music students.")
        {
            return new WizardGenerateRequest { ModelRunnerId = Runner.Id, Draft = new SubjectWizardDraft { Description = description } };
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            _Http.Dispose();
            Site.Dispose();
            Model.Dispose();
            await H.DisposeAsync();
        }
    }
}
