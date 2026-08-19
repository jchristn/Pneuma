namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Server settings routes. Only the system administrator may read or write the settings file.
    /// Secrets are masked on read; a masked value submitted on write preserves the stored secret.
    /// Most changes require a server restart to take effect (annotated in the read metadata).
    /// </summary>
    public class SettingsRoutes
    {
        #region Private-Members

        private const string SecretMask = "********";

        private readonly AppSettings _Settings;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate settings routes.</summary>
        /// <param name="settings">Live application settings (also the write target).</param>
        /// <param name="authz">Authorization service.</param>
        public SettingsRoutes(AppSettings settings, AuthorizationService authz)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            _Settings = settings;
            _Authz = authz;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/settings", ReadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Read server settings", "Settings"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.PUT, "/v1.0/settings", WriteAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Overwrite server settings", "Settings"));
        }

        #endregion

        #region Private-Methods

        private async Task ReadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!rc.IsAdmin)
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Settings are restricted to the system administrator.").ConfigureAwait(false);
                return;
            }

            AppSettings masked = Json.Deserialize<AppSettings>(Json.Serialize(_Settings)) ?? new AppSettings();
            MaskSecrets(masked);

            SettingsEnvelope envelope = new SettingsEnvelope
            {
                Settings = masked,
                Meta = BuildMeta()
            };
            await RouteHelper.SendJsonAsync(ctx, 200, envelope).ConfigureAwait(false);
        }

        private async Task WriteAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!rc.IsAdmin)
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Settings are restricted to the system administrator.").ConfigureAwait(false);
                return;
            }

            AppSettings? incoming = RouteHelper.ReadBody<AppSettings>(ctx);
            if (incoming == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A settings body is required.").ConfigureAwait(false);
                return;
            }

            RestoreMaskedSecrets(incoming);
            incoming.SourceFilePath = _Settings.SourceFilePath;

            string path = String.IsNullOrWhiteSpace(_Settings.SourceFilePath) ? "pneuma.json" : _Settings.SourceFilePath!;
            string json = System.Text.Json.JsonSerializer.Serialize(incoming, IndentedOptions());

            try
            {
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                await RouteHelper.SendErrorAsync(ctx, 500, "WriteFailed", "Could not write settings file: " + e.Message).ConfigureAwait(false);
                return;
            }

            // Apply the sections that are read live so they take effect without a restart.
            _Settings.Cors = incoming.Cors;
            _Settings.RequestHistory = incoming.RequestHistory;
            _Settings.Logging.LogHttpRequests = incoming.Logging.LogHttpRequests;

            SettingsEnvelope envelope = new SettingsEnvelope
            {
                Success = true,
                RestartRequired = true,
                Message = "Settings saved. Restart the server for all changes to take effect.",
                Meta = BuildMeta()
            };
            await RouteHelper.SendJsonAsync(ctx, 200, envelope).ConfigureAwait(false);
        }

        private void MaskSecrets(AppSettings s)
        {
            if (!String.IsNullOrEmpty(s.Auth.TokenSigningKey)) s.Auth.TokenSigningKey = SecretMask;
            if (s.Auth.AdminApiKeys != null && s.Auth.AdminApiKeys.Count > 0) s.Auth.AdminApiKeys = new List<string> { SecretMask };
            if (!String.IsNullOrEmpty(s.Database.Password)) s.Database.Password = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.RecallDb.BearerToken)) s.Integrations.RecallDb.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.Partio.BearerToken)) s.Integrations.Partio.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.Integrations.LiteGraph.BearerToken)) s.Integrations.LiteGraph.BearerToken = SecretMask;
            if (!String.IsNullOrEmpty(s.S3.AccessKey)) s.S3.AccessKey = SecretMask;
            if (!String.IsNullOrEmpty(s.S3.SecretKey)) s.S3.SecretKey = SecretMask;
            if (!String.IsNullOrEmpty(s.Seed.AdminPassword)) s.Seed.AdminPassword = SecretMask;
        }

        private void RestoreMaskedSecrets(AppSettings incoming)
        {
            if (incoming.Auth.TokenSigningKey == SecretMask) incoming.Auth.TokenSigningKey = _Settings.Auth.TokenSigningKey;
            if (incoming.Auth.AdminApiKeys != null && incoming.Auth.AdminApiKeys.Count == 1 && incoming.Auth.AdminApiKeys[0] == SecretMask)
                incoming.Auth.AdminApiKeys = _Settings.Auth.AdminApiKeys;
            if (incoming.Database.Password == SecretMask) incoming.Database.Password = _Settings.Database.Password;
            if (incoming.Integrations.RecallDb.BearerToken == SecretMask) incoming.Integrations.RecallDb.BearerToken = _Settings.Integrations.RecallDb.BearerToken;
            if (incoming.Integrations.Partio.BearerToken == SecretMask) incoming.Integrations.Partio.BearerToken = _Settings.Integrations.Partio.BearerToken;
            if (incoming.Integrations.LiteGraph.BearerToken == SecretMask) incoming.Integrations.LiteGraph.BearerToken = _Settings.Integrations.LiteGraph.BearerToken;
            if (incoming.S3.AccessKey == SecretMask) incoming.S3.AccessKey = _Settings.S3.AccessKey;
            if (incoming.S3.SecretKey == SecretMask) incoming.S3.SecretKey = _Settings.S3.SecretKey;
            if (incoming.Seed.AdminPassword == SecretMask) incoming.Seed.AdminPassword = _Settings.Seed.AdminPassword;
        }

        private SettingsMeta BuildMeta()
        {
            SettingsMeta meta = new SettingsMeta();
            meta.SecretFields = new List<string>
            {
                "auth.tokenSigningKey",
                "auth.adminApiKeys",
                "database.password",
                "integrations.recallDb.bearerToken",
                "integrations.partio.bearerToken",
                "integrations.liteGraph.bearerToken",
                "s3.accessKey",
                "s3.secretKey",
                "seed.adminPassword"
            };
            meta.Sections = new List<SettingsSectionMeta>
            {
                new SettingsSectionMeta { Key = "rest", Label = "Web Server", RequiresRestart = true },
                new SettingsSectionMeta { Key = "cors", Label = "CORS", RequiresRestart = false },
                new SettingsSectionMeta { Key = "logging", Label = "Logging", RequiresRestart = true },
                new SettingsSectionMeta { Key = "database", Label = "Database", RequiresRestart = true },
                new SettingsSectionMeta { Key = "auth", Label = "Authentication", RequiresRestart = true },
                new SettingsSectionMeta { Key = "requestHistory", Label = "Request History", RequiresRestart = false },
                new SettingsSectionMeta { Key = "ingestion", Label = "Ingestion", RequiresRestart = true },
                new SettingsSectionMeta { Key = "integrations", Label = "Integrations", RequiresRestart = true },
                new SettingsSectionMeta { Key = "s3", Label = "Object Storage (S3)", RequiresRestart = true },
                new SettingsSectionMeta { Key = "telemetry", Label = "Telemetry", RequiresRestart = true },
                new SettingsSectionMeta { Key = "seed", Label = "Seeding", RequiresRestart = true }
            };
            meta.SecretMask = SecretMask;
            return meta;
        }

        private static System.Text.Json.JsonSerializerOptions IndentedOptions()
        {
            System.Text.Json.JsonSerializerOptions options = new System.Text.Json.JsonSerializerOptions(Json.Options);
            options.WriteIndented = true;
            return options;
        }

        #endregion
    }
}
