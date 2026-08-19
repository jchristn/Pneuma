namespace Pneuma.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Implementations;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Composition root: loads settings, wires services, starts the server, runs maintenance, and
    /// handles graceful shutdown.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>Run the application.</summary>
        /// <param name="args">Command-line arguments.</param>
        public static void Run(string[] args)
        {
            RunAsync(args).GetAwaiter().GetResult();
        }

        #endregion

        #region Private-Methods

        private static async Task RunAsync(string[] args)
        {
            AppSettings settings = LoadSettings(args);
            LoggingModule logging = BuildLogging(settings);
            logging.Info("[Bootstrapper] starting Pneuma server (database: " + settings.Database.Type + ")");

            DatabaseDriverBase database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings.Database).ConfigureAwait(false);
            AuthenticationService authentication = new AuthenticationService(database, settings.Auth, logging);
            logging.Info("[Bootstrapper] database initialized; applying first-boot seed");
            Pneuma.Core.Database.TenantProvisionResult? seedAdmin = await FirstBootSeeder.SeedAsync(database, settings.Seed, authentication.Cipher).ConfigureAwait(false);
            if (seedAdmin != null && seedAdmin.CredentialCreated)
            {
                logging.Info("[Bootstrapper] default API key created for " + seedAdmin.User.Email
                    + " (accessKey=" + seedAdmin.AccessKey + ", secretKey=" + seedAdmin.SecretKey + "); this secret is shown once — store it now.");
            }

            AuthorizationService authorization = new AuthorizationService(database, logging);
            RequestHistoryCaptureService capture = new RequestHistoryCaptureService(database, settings.RequestHistory, logging);
            TelemetryService telemetry = new TelemetryService(settings.Telemetry, logging);

            IntegrationClients clients = BuildIntegrationClients(settings);
            IArtifactStore artifactStore = BuildArtifactStore(settings, logging);
            ModelHealthMonitor modelHealth = new ModelHealthMonitor(clients.Partio, logging);

            if (settings.Diagnostics.RunStartupProbes)
            {
                List<IServiceProbe> probes = new List<IServiceProbe>();
                if (clients.DocumentAtom is IServiceProbe documentAtomProbe) probes.Add(documentAtomProbe);
                if (clients.Partio is IServiceProbe partioProbe) probes.Add(partioProbe);
                if (clients.Verbex is IServiceProbe verbexProbe) probes.Add(verbexProbe);
                if (clients.Graph is IServiceProbe graphProbe) probes.Add(graphProbe);

                ExternalServiceDiagnosticsService diagnostics = new ExternalServiceDiagnosticsService(probes, logging);
                await diagnostics.RunAsync(settings.Diagnostics.FailFastOnStartupProbe).ConfigureAwait(false);
            }

            PneumaServer server = new PneumaServer(settings, database, authentication, authorization, capture, clients.Graph, clients.Vectors, clients.Verbex, clients.Partio, modelHealth, artifactStore, clients.Blobs, logging, telemetry);
            server.Start();

            if (settings.S3.Enabled)
            {
                try
                {
                    await artifactStore.EnsureBucketsAsync().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logging.Warn("[Bootstrapper] S3 bucket initialization failed (continuing): " + e.Message);
                }
            }

            try
            {
                LiteGraphInitializer liteGraphInit = new LiteGraphInitializer(
                    settings.Integrations.LiteGraph.Endpoint,
                    settings.Integrations.LiteGraph.BearerToken,
                    settings.Integrations.LiteGraph.TenantGuid ?? String.Empty,
                    clients.Graph,
                    logging);
                await liteGraphInit.InitializeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logging.Warn("[Bootstrapper] LiteGraph initialization failed (continuing): " + e.Message);
            }

            IContentFetcher fetcher = settings.Ingestion.UseHeadlessBrowser
                ? new PlaywrightContentFetcher(new HttpContentFetcher(), settings.Ingestion.BrowserNavigationTimeoutMs)
                : new HttpContentFetcher();
            IngestionProcessor processor = new IngestionProcessor(
                database, clients.DocumentAtom, clients.Partio, clients.Verbex, clients.Graph, clients.Vectors, clients.Blobs,
                artifactStore, fetcher, authentication.Cipher, settings.Ingestion, settings.Retrieval, logging, telemetry);
            IngestionWorkerService worker = new IngestionWorkerService(database, processor, settings.Ingestion, logging);

            using (CancellationTokenSource shutdown = new CancellationTokenSource())
            {
                worker.Start(shutdown.Token);
                modelHealth.Start(shutdown.Token);
                // Ensure default model endpoints exist in Partio when none are configured (create-only, so
                // operator edits persist across restarts). Runs in the background because it retries while
                // Partio finishes coming up.
                PartioEndpointInitializer partioInit = new PartioEndpointInitializer(clients.Partio, settings.Seed.OllamaBaseUrl, logging);
                Task partioSeed = Task.Run(() => partioInit.InitializeAsync(shutdown.Token), shutdown.Token);
                Task maintenance = MaintenanceLoopAsync(database, settings, logging, shutdown.Token);

                ManualResetEventSlim quit = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    quit.Set();
                };
                AppDomain.CurrentDomain.ProcessExit += (sender, e) => quit.Set();

                logging.Info("[Bootstrapper] running; press Ctrl+C to stop");
                quit.Wait();

                logging.Info("[Bootstrapper] shutting down");
                shutdown.Cancel();
                server.Stop();
                try { await maintenance.ConfigureAwait(false); } catch (OperationCanceledException) { }
                await database.DisposeAsync().ConfigureAwait(false);
                if (artifactStore is IDisposable disposableStore) disposableStore.Dispose();
            }
        }

        private static IArtifactStore BuildArtifactStore(AppSettings settings, LoggingModule logging)
        {
            if (!settings.S3.Enabled)
            {
                logging.Info("[Bootstrapper] S3 artifact storage disabled; using no-op artifact store");
                return new NullArtifactStore();
            }

            logging.Info("[Bootstrapper] S3 artifact storage enabled (endpoint: " + settings.S3.Endpoint + ")");
            return new S3ArtifactStore(
                settings.S3.Endpoint,
                settings.S3.Region,
                settings.S3.AccessKey,
                settings.S3.SecretKey,
                settings.S3.ForcePathStyle,
                settings.S3.Buckets.Source,
                settings.S3.Buckets.Atoms,
                settings.S3.Buckets.Chunks,
                settings.S3.Buckets.Embeddings,
                settings.S3.Buckets.Subgraph,
                logging);
        }

        private static IntegrationClients BuildIntegrationClients(AppSettings settings)
        {
            IntegrationsSettings integrations = settings.Integrations;
            IntegrationResilienceSettings resilience = integrations.Resilience;

            LiteGraphClient graphClient = new LiteGraphClient(
                integrations.LiteGraph.Endpoint,
                integrations.LiteGraph.BearerToken,
                integrations.LiteGraph.TenantGuid ?? String.Empty,
                integrations.LiteGraph.GraphGuid,
                resilience.TimeoutMilliseconds,
                resilience.MaxConcurrentRequests,
                resilience.RetryCount,
                resilience.RetryDelayMilliseconds);

            return new IntegrationClients
            {
                DocumentAtom = new DocumentAtomClient(
                    integrations.DocumentAtom.Endpoint,
                    resilience.TimeoutMilliseconds,
                    resilience.MaxConcurrentRequests,
                    resilience.RetryCount,
                    resilience.RetryDelayMilliseconds),
                Partio = new PartioClient(
                    integrations.Partio.Endpoint,
                    integrations.Partio.BearerToken ?? String.Empty,
                    integrations.Partio.EmbeddingEndpointId,
                    integrations.Partio.CompletionEndpointId,
                    integrations.Partio.TenantId,
                    resilience.TimeoutMilliseconds,
                    resilience.MaxConcurrentRequests,
                    resilience.RetryCount,
                    resilience.RetryDelayMilliseconds),
                Verbex = new VerbexClient(
                    integrations.Verbex.Endpoint,
                    integrations.Verbex.BearerToken ?? String.Empty,
                    integrations.Verbex.TenantId,
                    integrations.Verbex.IndexName,
                    resilience.TimeoutMilliseconds,
                    resilience.MaxConcurrentRequests,
                    resilience.RetryCount,
                    resilience.RetryDelayMilliseconds),
                Graph = graphClient,
                Vectors = new LiteGraphVectorRepository(
                    integrations.LiteGraph.Endpoint,
                    integrations.LiteGraph.BearerToken,
                    integrations.LiteGraph.TenantGuid ?? String.Empty,
                    graphClient,
                    resilience.TimeoutMilliseconds,
                    resilience.MaxConcurrentRequests,
                    resilience.RetryCount,
                    resilience.RetryDelayMilliseconds),
                Blobs = new DiskBlobStore(integrations.Blob.Directory)
            };
        }

        private static async Task MaintenanceLoopAsync(DatabaseDriverBase database, AppSettings settings, LoggingModule logging, CancellationToken token)
        {
            TimeSpan interval = TimeSpan.FromMinutes(settings.RequestHistory.PruneIntervalMinutes);
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                try
                {
                    DateTime cutoff = DateTime.UtcNow.AddDays(-settings.RequestHistory.RetentionDays);
                    await database.RequestHistory.PruneAsync(cutoff, token).ConfigureAwait(false);
                    await database.Sessions.DeleteExpiredAsync(DateTime.UtcNow, token).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    logging.Warn("[Bootstrapper] maintenance error: " + e.Message);
                }
            }
        }

        private static AppSettings LoadSettings(string[] args)
        {
            string path = Environment.GetEnvironmentVariable("PNEUMA_SETTINGS_FILE") ?? "pneuma.json";
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--config" || args[i] == "--settings") path = args[i + 1];
            }

            AppSettings settings;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                settings = Json.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            else
            {
                settings = new AppSettings();
            }

            settings.SourceFilePath = path;
            ApplyEnvironmentOverrides(settings);
            return settings;
        }

        private static void ApplyEnvironmentOverrides(AppSettings settings)
        {
            string? dbType = Environment.GetEnvironmentVariable("PNEUMA_DB_TYPE");
            if (!String.IsNullOrWhiteSpace(dbType) && Enum.TryParse<DatabaseTypeEnum>(dbType, true, out DatabaseTypeEnum parsed))
                settings.Database.Type = parsed;

            string? host = Environment.GetEnvironmentVariable("PNEUMA_DB_HOST");
            if (!String.IsNullOrWhiteSpace(host)) settings.Database.Hostname = host;

            string? port = Environment.GetEnvironmentVariable("PNEUMA_DB_PORT");
            if (!String.IsNullOrWhiteSpace(port) && Int32.TryParse(port, out int p)) settings.Database.Port = p;

            string? name = Environment.GetEnvironmentVariable("PNEUMA_DB_DATABASE");
            if (!String.IsNullOrWhiteSpace(name)) settings.Database.DatabaseName = name;

            string? user = Environment.GetEnvironmentVariable("PNEUMA_DB_USERNAME");
            if (!String.IsNullOrWhiteSpace(user)) settings.Database.Username = user;

            string? password = Environment.GetEnvironmentVariable("PNEUMA_DB_PASSWORD");
            if (!String.IsNullOrWhiteSpace(password)) settings.Database.Password = password;

            string? schema = Environment.GetEnvironmentVariable("PNEUMA_DB_SCHEMA");
            if (!String.IsNullOrWhiteSpace(schema)) settings.Database.Schema = schema;

            string? signingKey = Environment.GetEnvironmentVariable("PNEUMA_AUTH_SIGNING_KEY");
            if (!String.IsNullOrWhiteSpace(signingKey)) settings.Auth.TokenSigningKey = signingKey;

            string? adminEmail = Environment.GetEnvironmentVariable("PNEUMA_ADMIN_EMAIL");
            if (!String.IsNullOrWhiteSpace(adminEmail)) settings.Seed.AdminEmail = adminEmail;

            string? adminPassword = Environment.GetEnvironmentVariable("PNEUMA_ADMIN_PASSWORD");
            if (!String.IsNullOrWhiteSpace(adminPassword)) settings.Seed.AdminPassword = adminPassword;

            string? ollamaBaseUrl = Environment.GetEnvironmentVariable("PNEUMA_OLLAMA_BASE_URL");
            if (!String.IsNullOrWhiteSpace(ollamaBaseUrl)) settings.Seed.OllamaBaseUrl = ollamaBaseUrl;
        }

        private static LoggingModule BuildLogging(AppSettings settings)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.MinimumSeverity = (Severity)settings.Logging.MinimumSeverity;
            logging.Settings.EnableConsole = settings.Logging.ConsoleLogging;

            if (settings.Logging.FileLogging)
            {
                Directory.CreateDirectory(settings.Logging.LogDirectory);
                logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                logging.Settings.LogFilename = Path.Combine(settings.Logging.LogDirectory, settings.Logging.LogFilename);
            }

            return logging;
        }

        #endregion
    }
}
