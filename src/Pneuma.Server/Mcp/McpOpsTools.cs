namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP operations tools that mirror the admin/observability REST surface for request history, server
    /// settings (redacted), and model-runner endpoint health. All of these are restricted to the system
    /// administrator by <see cref="McpToolAuthorization"/>, matching the REST twins.
    /// </summary>
    public class McpOpsTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AppSettings _Settings;
        private readonly IPartioClient _Partio;
        private readonly ModelHealthMonitor _Health;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the operations tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="settings">Live application settings (returned redacted).</param>
        /// <param name="partio">Partio client used to enumerate model endpoints.</param>
        /// <param name="health">Model health monitor providing per-endpoint status.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public McpOpsTools(DatabaseDriverBase db, AppSettings settings, IPartioClient partio, ModelHealthMonitor health)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Partio = partio ?? throw new ArgumentNullException(nameof(partio));
            _Health = health ?? throw new ArgumentNullException(nameof(health));
        }

        #endregion

        #region Public-Methods

        /// <summary>Enumerate captured request-history entries as small summaries, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateRequestHistoryAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            int pageSize = Math.Clamp(query.MaxResults, 1, 1000);
            int pageNumber = (query.Skip / pageSize) + 1;

            RequestHistoryFilter filter = new RequestHistoryFilter
            {
                TenantId = GetOptionalString(arguments, "tenantId"),
                UserId = GetOptionalString(arguments, "userId"),
                Method = GetOptionalString(arguments, "method"),
                PathContains = GetOptionalString(arguments, "pathContains"),
                PageNumber = pageNumber,
                PageSize = pageSize
            };
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("statusCode", out JsonElement sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out int status)) filter.StatusCode = status;

            RequestHistoryPage page = await _Db.RequestHistory.EnumerateAsync(filter, token).ConfigureAwait(false);

            List<object> summaries = new List<object>();
            foreach (RequestHistoryEntry entry in page.Items)
            {
                summaries.Add(new { id = entry.Id, method = entry.Method, path = entry.Path, statusCode = entry.StatusCode, tenantId = entry.TenantId, durationMs = entry.DurationMs, createdUtc = entry.CreatedUtc });
            }

            int skip = (page.PageNumber - 1) * page.PageSize;
            long remaining = Math.Max(0, page.TotalCount - (skip + page.Items.Count));
            bool endOfResults = skip + page.Items.Count >= page.TotalCount;
            return McpJsonRpc.BuildPage(page.PageSize, skip, page.TotalCount, remaining, endOfResults, summaries);
        }

        /// <summary>Fetch a single request-history entry with its full captured detail.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The entry, or null when an error response was already sent.</returns>
        public async Task<object?> GetRequestHistoryAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string entryId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(entryId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            RequestHistoryEntry? entry = await _Db.RequestHistory.ReadAsync(null, entryId, token).ConfigureAwait(false);
            if (entry == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Request history entry not found.").ConfigureAwait(false); return null; }
            return entry;
        }

        /// <summary>Summarize request-history over an optional filter (counts, status breakdown, latency).</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The summary.</returns>
        public async Task<object> RequestHistorySummaryAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            RequestHistoryFilter filter = new RequestHistoryFilter
            {
                TenantId = GetOptionalString(arguments, "tenantId"),
                Method = GetOptionalString(arguments, "method"),
                PathContains = GetOptionalString(arguments, "pathContains")
            };
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("statusCode", out JsonElement sc) && sc.ValueKind == JsonValueKind.Number && sc.TryGetInt32(out int status)) filter.StatusCode = status;
            RequestHistorySummary summary = await _Db.RequestHistory.SummarizeAsync(filter, token).ConfigureAwait(false);
            return summary;
        }

        /// <summary>Return the server settings with all secret fields redacted.</summary>
        /// <returns>The redacted settings snapshot.</returns>
        public object GetSettings()
        {
            AppSettings masked = Json.Deserialize<AppSettings>(Json.Serialize(_Settings)) ?? new AppSettings();
            SettingsRedactor.Mask(masked);
            return masked;
        }

        /// <summary>Enumerate model-endpoint health (embedding + completion) as small summaries, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateModelRunnerHealthAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            List<ModelEndpointHealthDto> health = new List<ModelEndpointHealthDto>();
            List<PartioEndpoint> embedding = await _Partio.ListEmbeddingEndpointsAsync(token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in embedding) health.Add(_Health.BuildStatus(endpoint.Id, endpoint.Name, "Embedding", endpoint.Endpoint));
            List<PartioEndpoint> completion = await _Partio.ListCompletionEndpointsAsync(token).ConfigureAwait(false);
            foreach (PartioEndpoint endpoint in completion) health.Add(_Health.BuildStatus(endpoint.Id, endpoint.Name, "Completion", endpoint.Endpoint));

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            int total = health.Count;
            int skip = Math.Max(0, query.Skip);
            int take = Math.Clamp(query.MaxResults, 1, 1000);
            List<object> pageObjects = new List<object>();
            for (int i = skip; i < total && i < skip + take; i++) pageObjects.Add(health[i]);
            long remaining = Math.Max(0, total - (skip + pageObjects.Count));
            bool endOfResults = skip + pageObjects.Count >= total;
            return McpJsonRpc.BuildPage(take, skip, total, remaining, endOfResults, pageObjects);
        }

        /// <summary>Fetch the health of a single model endpoint by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The health status, or null when an error response was already sent.</returns>
        public async Task<object?> GetModelRunnerHealthAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string endpointId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(endpointId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }

            string display = "Embedding";
            PartioEndpoint? endpoint = await _Partio.ReadEndpointAsync("embedding", endpointId, token).ConfigureAwait(false);
            if (endpoint == null)
            {
                endpoint = await _Partio.ReadEndpointAsync("completion", endpointId, token).ConfigureAwait(false);
                display = "Completion";
            }
            if (endpoint == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Model endpoint not found.").ConfigureAwait(false); return null; }
            return _Health.BuildStatus(endpoint.Id, endpoint.Name, display, endpoint.Endpoint);
        }

        #endregion

        #region Private-Methods

        private static string? GetOptionalString(JsonElement args, string name)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        #endregion
    }
}
