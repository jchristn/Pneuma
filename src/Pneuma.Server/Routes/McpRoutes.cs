namespace Pneuma.Server.Routes
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Security;
    using Pneuma.Server.Mcp;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Model Context Protocol (MCP) endpoint, served in-process by the Pneuma host over JSON-RPC 2.0 at
    /// <c>POST /mcp</c>. Every tool call passes through the same <c>AuthenticateRequest</c> hook and
    /// <see cref="AuthorizationService"/> as the REST API, so the same bearer and API-key credentials
    /// apply and every decision is audited identically. This class owns only transport and top-level
    /// method routing; tool metadata lives in <see cref="McpToolCatalog"/>, JSON-RPC helpers in
    /// <see cref="McpJsonRpc"/>, and tool execution in <see cref="McpToolInvoker"/>.
    /// </summary>
    public class McpRoutes
    {
        #region Private-Members

        private readonly McpToolInvoker _Invoker;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate MCP routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="defaultCollectionId">Default collection id used when a request specifies none.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="query">Shared grounded query service for the grounded-answer tool.</param>
        /// <param name="gate">Model-runner concurrency gate applied to the grounded-answer tool.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public McpRoutes(DatabaseDriverBase db, AuthorizationService authz, IInvertedIndex search, ICollectionStore collections, string? defaultCollectionId, IGraphRepositoryFactory graphFactory, GroundedQueryService query, ModelRunnerGate gate)
        {
            _Invoker = new McpToolInvoker(db, authz, search, collections, defaultCollectionId, graphFactory, query, gate);
        }

        #endregion

        #region Public-Methods

        /// <summary>Register the MCP route.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/mcp", HandleAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("MCP JSON-RPC endpoint (tools/list, tools/call)", "MCP"));
        }

        #endregion

        #region Private-Methods

        private async Task HandleAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            string body = ctx.Request.DataAsString ?? String.Empty;

            string method;
            object? id = null;
            JsonElement paramsElement = default;
            bool hasParams = false;

            try
            {
                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement root = doc.RootElement;
                    id = McpJsonRpc.ExtractId(root);
                    method = root.TryGetProperty("method", out JsonElement m) && m.ValueKind == JsonValueKind.String ? (m.GetString() ?? String.Empty) : String.Empty;
                    if (root.TryGetProperty("params", out JsonElement p))
                    {
                        paramsElement = p.Clone();
                        hasParams = true;
                    }
                }
            }
            catch (JsonException)
            {
                await McpJsonRpc.SendErrorAsync(ctx, null, -32700, "Parse error.").ConfigureAwait(false);
                return;
            }

            if (String.IsNullOrEmpty(method))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32600, "Invalid request: missing method.").ConfigureAwait(false);
                return;
            }

            switch (method)
            {
                case "initialize":
                    await McpJsonRpc.SendResultAsync(ctx, id, McpToolCatalog.BuildInitializeResult()).ConfigureAwait(false);
                    return;
                case "ping":
                    await McpJsonRpc.SendResultAsync(ctx, id, new { }).ConfigureAwait(false);
                    return;
                case "notifications/initialized":
                    ctx.Response.StatusCode = 202;
                    await ctx.Response.Send().ConfigureAwait(false);
                    return;
                case "tools/list":
                    await McpJsonRpc.SendResultAsync(ctx, id, new { tools = McpToolCatalog.BuildToolList() }).ConfigureAwait(false);
                    return;
                case "tools/call":
                    await _Invoker.HandleToolCallAsync(ctx, rc, id, hasParams ? paramsElement : default).ConfigureAwait(false);
                    return;
                default:
                    await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Method not found: " + method).ConfigureAwait(false);
                    return;
            }
        }

        #endregion
    }
}
