namespace Pneuma.Server.Settings
{
    using System;
    using System.Text.Json.Serialization;
    using Pneuma.Core.Database;

    /// <summary>
    /// Root application settings.
    /// </summary>
    public class AppSettings
    {
        #region Public-Members

        /// <summary>
        /// Filesystem path the settings were loaded from (or the default target for writes).
        /// Not serialized into the settings file.
        /// </summary>
        [JsonIgnore]
        public string? SourceFilePath { get; set; } = null;

        /// <summary>UTC timestamp the settings file was created.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Web server settings.</summary>
        public RestSettings Rest { get; set; } = new RestSettings();

        /// <summary>CORS settings.</summary>
        public CorsSettings Cors { get; set; } = new CorsSettings();

        /// <summary>Logging settings.</summary>
        public LoggingSettings Logging { get; set; } = new LoggingSettings();

        /// <summary>Database settings.</summary>
        public DatabaseSettings Database { get; set; } = new DatabaseSettings();

        /// <summary>Authentication settings.</summary>
        public AuthSettings Auth { get; set; } = new AuthSettings();

        /// <summary>Request history capture settings.</summary>
        public RequestHistorySettings RequestHistory { get; set; } = new RequestHistorySettings();

        /// <summary>Ingestion worker settings.</summary>
        public IngestionSettings Ingestion { get; set; } = new IngestionSettings();

        /// <summary>External integration settings.</summary>
        public IntegrationsSettings Integrations { get; set; } = new IntegrationsSettings();

        /// <summary>Retrieval settings (inverted-index use, graph-neighbor expansion, vector search).</summary>
        public RetrievalSettings Retrieval { get; set; } = new RetrievalSettings();

        /// <summary>Model-runner concurrency settings (concurrency cap, queue depth before HTTP 429).</summary>
        public ModelRunnerSettings ModelRunner { get; set; } = new ModelRunnerSettings();

        /// <summary>S3-compatible object storage (Less3) settings and per-stage bucket names.</summary>
        public S3Settings S3 { get; set; } = new S3Settings();

        /// <summary>Telemetry settings.</summary>
        public TelemetrySettings Telemetry { get; set; } = new TelemetrySettings();

        /// <summary>Startup diagnostics settings.</summary>
        public DiagnosticsSettings Diagnostics { get; set; } = new DiagnosticsSettings();

        /// <summary>First-boot seeding options.</summary>
        public SeedOptions Seed { get; set; } = new SeedOptions();

        #endregion
    }
}
