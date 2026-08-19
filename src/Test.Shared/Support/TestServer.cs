namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Storage;
    using Pneuma.Server;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using SyslogLogging;

    /// <summary>
    /// Boots a real PneumaServer in-process on a free loopback port with a fresh seeded SQLite database
    /// and in-memory integration fakes, for HTTP/auth/RBAC route tests. No ingestion worker runs.
    /// </summary>
    public sealed class TestServer : IAsyncDisposable
    {
        #region Public-Members

        /// <summary>Base URL of the running server.</summary>
        public string BaseUrl { get; private set; } = String.Empty;

        /// <summary>The underlying database driver.</summary>
        public DatabaseDriverBase Database { get; private set; } = null!;

        /// <summary>The in-memory RecallDB fake the server is wired to (tenants, collections, documents).</summary>
        public FakeRecallDbClient Recall { get; private set; } = null!;

        #endregion

        #region Private-Members

        private PneumaServer _Server = null!;
        private string _DbFile = String.Empty;
        private string _SettingsFile = String.Empty;

        #endregion

        #region Public-Methods

        /// <summary>Create and start a test server.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A running test server.</returns>
        public static async Task<TestServer> CreateAsync(CancellationToken token = default)
        {
            TestServer instance = new TestServer();
            await instance.StartAsync(token).ConfigureAwait(false);
            return instance;
        }

        /// <summary>Stop the server and remove the temp database.</summary>
        /// <returns>A task.</returns>
        public async ValueTask DisposeAsync()
        {
            try { _Server.Stop(); } catch (Exception) { }
            try { await Database.DisposeAsync().ConfigureAwait(false); } catch (Exception) { }
            try { if (File.Exists(_DbFile)) File.Delete(_DbFile); } catch (Exception) { }
            try { if (File.Exists(_SettingsFile)) File.Delete(_SettingsFile); } catch (Exception) { }
        }

        #endregion

        #region Private-Methods

        private async Task StartAsync(CancellationToken token)
        {
            int port = FreePort();
            string dir = Path.Combine(Path.GetTempPath(), "pneuma-tests");
            Directory.CreateDirectory(dir);
            _DbFile = Path.Combine(dir, "pneuma-api-" + Guid.NewGuid().ToString("N") + ".db");
            _SettingsFile = Path.Combine(dir, "pneuma-settings-" + Guid.NewGuid().ToString("N") + ".json");

            AppSettings settings = new AppSettings();
            settings.SourceFilePath = _SettingsFile;
            settings.Rest.Hostname = "127.0.0.1";
            settings.Rest.Port = port;
            settings.Database.Type = DatabaseTypeEnum.Sqlite;
            settings.Database.Filename = _DbFile;
            settings.Telemetry.Enabled = false;
            settings.Logging.ConsoleLogging = false;
            settings.Logging.FileLogging = false;

            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = false;

            Database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings.Database, token).ConfigureAwait(false);
            await FirstBootSeeder.SeedAsync(Database, settings.Seed, new Pneuma.Core.Security.Aes256Cipher(settings.Auth.TokenSigningKey), token).ConfigureAwait(false);

            AuthenticationService authentication = new AuthenticationService(Database, settings.Auth, logging);
            AuthorizationService authorization = new AuthorizationService(Database, logging);
            RequestHistoryCaptureService capture = new RequestHistoryCaptureService(Database, settings.RequestHistory, logging);
            TelemetryService telemetry = new TelemetryService(settings.Telemetry, logging);

            DiskBlobStore blobs = new DiskBlobStore(Path.Combine(dir, "blobs-" + Guid.NewGuid().ToString("N")));
            FakePartioClient partio = new FakePartioClient();
            ModelHealthMonitor modelHealth = new ModelHealthMonitor(partio, logging);
            FakeRecallDbClient recall = new FakeRecallDbClient();
            Recall = recall;
            TenantProvisioningService provisioning = new TenantProvisioningService(
                new List<ITenantProvisioner>
                {
                    new RecallDbTenantProvisioner(recall, "default", 8),
                    new LiteGraphTenantProvisioner(Database, new FakeLiteGraphTenantAdmin())
                }, logging);
            _Server = new PneumaServer(settings, Database, authentication, authorization, capture, new FakeGraphRepositoryFactory(new FakeLiteGraphClient()), recall, recall, recall, partio, provisioning, modelHealth, new NullArtifactStore(), blobs, logging, telemetry);
            _Server.Start();

            BaseUrl = "http://127.0.0.1:" + port;
        }

        private static int FreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        #endregion
    }
}
