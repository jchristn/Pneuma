namespace Pneuma.Server.Mcp
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// Dispatches an MCP <c>tools/call</c> to the correct tool after authorizing it against the same
    /// <see cref="AuthorizationService"/> as the REST API. Tool implementations live in
    /// <see cref="McpEntityTools"/> (relational entities) and <see cref="McpGraphTools"/> (graph and
    /// grounded retrieval); this class owns only the name-to-tool routing, authorization, and the
    /// success/error envelope.
    /// </summary>
    public class McpToolInvoker
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly McpEntityTools _Entities;
        private readonly McpGraphTools _GraphTools;
        private readonly ModelRunnerGate _Gate;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tool invoker.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="defaultCollectionId">Default collection id used when a request specifies none.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="query">Shared grounded query service.</param>
        /// <param name="gate">Model-runner concurrency gate applied to the grounded-answer tool.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public McpToolInvoker(DatabaseDriverBase db, AuthorizationService authz, IInvertedIndex search, ICollectionStore collections, string? defaultCollectionId, IGraphRepositoryFactory graphFactory, GroundedQueryService query, ModelRunnerGate gate)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            _Authz = authz;
            _Entities = new McpEntityTools(db);
            _GraphTools = new McpGraphTools(search, collections, defaultCollectionId, graphFactory, query);
            _Gate = gate;
        }

        #endregion

        #region Public-Methods

        /// <summary>Authorize, dispatch, and respond to a <c>tools/call</c> request.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="paramsElement">The JSON-RPC <c>params</c> element.</param>
        /// <returns>A task.</returns>
        public async Task HandleToolCallAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement paramsElement)
        {
            string toolName = paramsElement.ValueKind == JsonValueKind.Object && paramsElement.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                ? (n.GetString() ?? String.Empty)
                : String.Empty;

            JsonElement arguments = default;
            if (paramsElement.ValueKind == JsonValueKind.Object && paramsElement.TryGetProperty("arguments", out JsonElement a))
            {
                arguments = a;
            }

            if (String.IsNullOrEmpty(toolName))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: a tool name is required.").ConfigureAwait(false);
                return;
            }

            if (!await AuthorizeToolAsync(rc, toolName, ctx.Token).ConfigureAwait(false))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32000, "Not permitted.").ConfigureAwait(false);
                return;
            }

            object? toolResult;
            switch (toolName)
            {
                case "pneuma_capabilities":
                    toolResult = McpToolCatalog.BuildCapabilities();
                    break;
                case "pneuma_enumerate_subjects":
                    toolResult = await _Entities.EnumerateSubjectsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_subject":
                    toolResult = await _Entities.GetSubjectAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_jobs":
                    toolResult = await _Entities.EnumerateJobsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_job":
                    toolResult = await _Entities.GetJobAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_links":
                    toolResult = await _Entities.EnumerateLinksAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_link":
                    toolResult = await _Entities.GetLinkAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_search":
                    toolResult = await _GraphTools.SearchAsync(rc.TenantId ?? String.Empty, arguments, null, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_node":
                    toolResult = await _GraphTools.GetNodeAsync(rc.TenantId ?? String.Empty, ctx, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_neighbors":
                    toolResult = await _GraphTools.GetNeighborsAsync(rc.TenantId ?? String.Empty, ctx, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_query":
                {
                    // Admit grounded-answer (model-runner) usage through the concurrency gate; a saturated
                    // system rejects with a JSON-RPC error rather than piling onto the model runner.
                    IDisposable lease;
                    try
                    {
                        lease = await _Gate.AcquireAsync(ctx.Token).ConfigureAwait(false);
                    }
                    catch (ModelRunnerBusyException busy)
                    {
                        await McpJsonRpc.SendErrorAsync(ctx, id, -32000, busy.Message).ConfigureAwait(false);
                        return;
                    }

                    using (lease)
                    {
                        if (McpJsonRpc.IsStreamRequested(arguments))
                        {
                            // Streamable-HTTP: the tool sends its own SSE response, whose final event is the result.
                            await _GraphTools.StreamGroundedQueryAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                            return;
                        }
                        toolResult = await _GraphTools.GroundedQueryAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                        if (toolResult == null) return; // error already sent
                    }
                    break;
                }
                default:
                    await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Unknown tool: " + toolName).ConfigureAwait(false);
                    return;
            }

            object callResult = new
            {
                content = new[] { new { type = "text", text = Json.Serialize(toolResult) } },
                isError = false
            };
            await McpJsonRpc.SendResultAsync(ctx, id, callResult).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<bool> AuthorizeToolAsync(RequestContext rc, string toolName, CancellationToken token)
        {
            return await McpToolAuthorization.AuthorizeAsync(_Authz, rc, toolName, token).ConfigureAwait(false);
        }

        #endregion
    }
}
