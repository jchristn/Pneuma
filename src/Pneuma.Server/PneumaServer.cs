namespace Pneuma.Server
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Mcp;
    using Pneuma.Server.Routes;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Watson 7 server host for Pneuma. Wires the authentication hook, CORS preflight and post-routing
    /// (including request capture), OpenAPI exposure, and the feature route registrars.
    /// </summary>
    public class PneumaServer
    {
        #region Private-Members

        private readonly AppSettings _Settings;
        private readonly DatabaseDriverBase _Database;
        private readonly AuthenticationService _Authentication;
        private readonly AuthorizationService _Authorization;
        private readonly RequestHistoryCaptureService _Capture;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly IVectorRepository _Vectors;
        private readonly IInvertedIndex _Search;
        private readonly ICollectionStore _Collections;
        private readonly IPartioClient _Partio;
        private readonly TenantProvisioningService _Provisioning;
        private readonly ModelHealthMonitor _ModelHealth;
        private readonly ModelRunnerGate _ModelRunnerGate;
        private readonly IArtifactStore _Artifacts;
        private readonly IBlobStore _Blobs;
        private readonly LoggingModule _Logging;
        private readonly TelemetryService _Telemetry;
        private readonly Webserver _Server;
        private readonly string _Header = "[PneumaServer] ";

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the server host.</summary>
        /// <param name="settings">Application settings.</param>
        /// <param name="database">Database driver.</param>
        /// <param name="authentication">Authentication service.</param>
        /// <param name="authorization">Authorization service.</param>
        /// <param name="capture">Request capture service.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store (RecallDB).</param>
        /// <param name="partio">Partio client.</param>
        /// <param name="provisioning">Tenant provisioning service (subordinate-service resources on tenant creation).</param>
        /// <param name="artifacts">Per-stage S3 artifact store, used by the artifact-view endpoints.</param>
        /// <param name="blobs">Blob store, used for cascading deletion of a link's raw ingested blobs.</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="telemetry">Shared telemetry service for request and pipeline spans.</param>
        public PneumaServer(
            AppSettings settings,
            DatabaseDriverBase database,
            AuthenticationService authentication,
            AuthorizationService authorization,
            RequestHistoryCaptureService capture,
            IGraphRepositoryFactory graphFactory,
            IVectorRepository vectors,
            IInvertedIndex search,
            ICollectionStore collections,
            IPartioClient partio,
            TenantProvisioningService provisioning,
            ModelHealthMonitor modelHealth,
            IArtifactStore artifacts,
            IBlobStore blobs,
            LoggingModule logging,
            TelemetryService telemetry)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (database == null) throw new ArgumentNullException(nameof(database));
            if (authentication == null) throw new ArgumentNullException(nameof(authentication));
            if (authorization == null) throw new ArgumentNullException(nameof(authorization));
            if (capture == null) throw new ArgumentNullException(nameof(capture));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (vectors == null) throw new ArgumentNullException(nameof(vectors));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (provisioning == null) throw new ArgumentNullException(nameof(provisioning));
            if (modelHealth == null) throw new ArgumentNullException(nameof(modelHealth));
            if (artifacts == null) throw new ArgumentNullException(nameof(artifacts));
            if (blobs == null) throw new ArgumentNullException(nameof(blobs));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            if (telemetry == null) throw new ArgumentNullException(nameof(telemetry));

            _Settings = settings;
            _Database = database;
            _Authentication = authentication;
            _Authorization = authorization;
            _Capture = capture;
            _GraphFactory = graphFactory;
            _Vectors = vectors;
            _Search = search;
            _Collections = collections;
            _Partio = partio;
            _Provisioning = provisioning;
            _ModelHealth = modelHealth;
            _ModelRunnerGate = new ModelRunnerGate(_Settings.ModelRunner.MaxConcurrentRequests, _Settings.ModelRunner.MaxQueueDepth);
            _Artifacts = artifacts;
            _Blobs = blobs;
            _Logging = logging;
            _Telemetry = telemetry;

            WebserverSettings webserverSettings = new WebserverSettings(
                _Settings.Rest.Hostname,
                _Settings.Rest.Port,
                _Settings.Rest.Ssl);

            _Server = new Webserver(webserverSettings, DefaultRouteAsync);
        }

        #endregion

        #region Public-Members

        /// <summary>The authorization service, exposed for route registrars.</summary>
        public AuthorizationService Authorization { get { return _Authorization; } }

        #endregion

        #region Public-Methods

        /// <summary>Configure and start the server.</summary>
        public void Start()
        {
            ConfigureServer();
            ConfigureRoutes();
            _Server.Start();
            _Logging.Info(_Header + "listening on " + _Settings.Rest.Hostname + ":" + _Settings.Rest.Port);
        }

        /// <summary>Stop the server.</summary>
        public void Stop()
        {
            _Server.Stop();
            _ModelRunnerGate.Dispose();
            _Telemetry.Dispose();
        }

        #endregion

        #region Private-Methods

        private void ConfigureServer()
        {
            _Server.Routes.AuthenticateRequest = _Authentication.AuthenticateRequestAsync;
            _Server.Routes.Preflight = PreflightAsync;
            _Server.Routes.PostRouting = PostRoutingAsync;
            _Server.UseOpenApi();
        }

        private void ConfigureRoutes()
        {
            new HealthRoutes("0.1.0").Register(_Server);
            new AuthRoutes(_Database, _Authentication).Register(_Server);
            new TenantRoutes(_Database, _Authorization, _Authentication.Cipher, _Provisioning).Register(_Server);
            new UserRoutes(_Database, _Authorization).Register(_Server);
            new CredentialRoutes(_Database, _Authorization, _Authentication).Register(_Server);
            new RequestHistoryRoutes(_Database).Register(_Server);
            new ChatHistoryRoutes(_Database, _Authorization).Register(_Server);
            new SettingsRoutes(_Settings, _Authorization).Register(_Server);
            new RoleRoutes(_Database, _Authorization).Register(_Server);
            new PermissionRoutes(_Database, _Authorization).Register(_Server);
            new AssignmentRoutes(_Database, _Authorization).Register(_Server);
            new AuditRoutes(_Database, _Authorization).Register(_Server);
            CascadeDeletionService cascade = new CascadeDeletionService(_Database, _Artifacts, _Vectors, _GraphFactory, _Blobs);
            new SubjectRoutes(_Database, _Authorization, cascade).Register(_Server);
            new SubjectLinkRoutes(_Database, _Authorization, _Artifacts, cascade, _Collections).Register(_Server);
            new IngestionJobRoutes(_Database, _Authorization, cascade).Register(_Server);
            new IngestionEndpointRoutes(_Partio, _Authorization).Register(_Server);
            new CollectionRoutes(_Authorization, _Collections).Register(_Server);
            new ModelRunnerRoutes(_Partio, _Authorization, _ModelHealth).Register(_Server);
            new PromptRoutes(_Database, _Authorization).Register(_Server);
            new GraphRoutes(_Database, _Authorization, _GraphFactory).Register(_Server);
            new SearchRoutes(_Database, _Authorization, _Search, _Collections, _Settings.Retrieval.DefaultCollectionId, _GraphFactory).Register(_Server);
            GroundedQueryService groundedQuery = new GroundedQueryService(_Database, _Search, _Collections, _GraphFactory, _Vectors, _Partio, _Settings.Retrieval, _Authentication.Cipher, _Logging);
            new QueryRoutes(_Authorization, groundedQuery, _ModelRunnerGate, _Logging).Register(_Server);
            new McpRoutes(_Database, _Authorization, _Search, _Collections, _Settings.Retrieval.DefaultCollectionId, _GraphFactory, groundedQuery, _ModelRunnerGate).Register(_Server);
            PneumaToolExecutor toolExecutor = new PneumaToolExecutor(_Database, _Authorization, _Search, _Collections, _Settings.Retrieval.DefaultCollectionId, _GraphFactory, groundedQuery);
            AgenticChatService agenticChat = new AgenticChatService(_Database, groundedQuery, toolExecutor, _Authentication.Cipher, _Settings.Retrieval.ChatMaxToolIterations, _Logging);
            new ChatRoutes(_Authorization, agenticChat, _ModelRunnerGate, _Logging).Register(_Server);
        }

        private async Task PreflightAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 200;
            AddCorsHeaders(ctx);
            ctx.Response.Headers.Add("Access-Control-Max-Age", _Settings.Cors.MaxAgeSeconds.ToString());
            await ctx.Response.Send().ConfigureAwait(false);
        }

        private async Task PostRoutingAsync(HttpContextBase ctx)
        {
            ctx.Timestamp.End = DateTime.UtcNow;
            AddCorsHeaders(ctx);

            string method = ctx.Request.Method.ToString();
            string path = ctx.Request.Url.RawWithoutQuery ?? ctx.Request.Url.RawWithQuery;
            int status = ctx.Response.StatusCode;
            double seconds = (ctx.Timestamp.TotalMs ?? 0) / 1000.0;

            PneumaMetrics.RecordHttpRequest(method, RouteNormalizer.Normalize(path), status, seconds);
            _Telemetry.RecordRequest(method, path, status);

            if (_Settings.RequestHistory.Enabled)
            {
                _Capture.Capture(ctx);
            }

            if (_Settings.Logging.LogHttpRequests)
            {
                double ms = ctx.Timestamp.TotalMs ?? 0;
                _Logging.Debug(_Header + ctx.Request.Method + " " + ctx.Request.Url.RawWithQuery + " " +
                    ctx.Response.StatusCode + " (" + ms.ToString("F2") + "ms)");
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }

        private void AddCorsHeaders(HttpContextBase ctx)
        {
            ctx.Response.Headers.Add("Access-Control-Allow-Origin", _Settings.Cors.AllowOrigins);
            ctx.Response.Headers.Add("Access-Control-Allow-Methods", _Settings.Cors.AllowMethods);
            ctx.Response.Headers.Add("Access-Control-Allow-Headers", _Settings.Cors.AllowHeaders);
        }

        private async Task DefaultRouteAsync(HttpContextBase ctx)
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send("{\"error\":\"NotFound\",\"message\":\"No matching route.\"}").ConfigureAwait(false);
        }

        #endregion
    }
}
