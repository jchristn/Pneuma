namespace Pneuma.Server.Mcp
{
    using System;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Ingestion.Deletion;
    using Pneuma.Core.Ingestion.Pipeline;
    using Pneuma.Core.Ingestion.Prompts;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Core.Storage;
    using Pneuma.Server.Services;
    using Pneuma.Server.Settings;
    using SyslogLogging;
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
        #region Public-Members

        /// <summary>Crawl plan and crawl operation tools; null disables them.</summary>
        public McpCrawlTools? Crawl { get; set; } = null;

        /// <summary>Ontology governance tools; null disables them.</summary>
        public McpOntologyTools? Ontology { get; set; } = null;

        /// <summary>Link refresh service for <c>pneuma_set_link_refresh</c>'s "refresh now"; null checks nothing.</summary>
        public Pneuma.Core.Ingestion.Refresh.LinkRefreshService? LinkRefresh
        {
            get { return _Entities.LinkRefresh; }
            set { _Entities.LinkRefresh = value; }
        }

        #endregion

        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly McpEntityTools _Entities;
        private readonly McpGraphTools _GraphTools;
        private readonly McpManagementTools _Management;
        private readonly McpOpsTools _Ops;
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
        /// <param name="logging">Logging module (used by the eval management tools).</param>
        /// <param name="settings">Live application settings (returned redacted by the settings tool).</param>
        /// <param name="health">Model health monitor providing per-endpoint status.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        /// <param name="blobs">Blob store for pushed content; null disables the content push tool.</param>
        public McpToolInvoker(DatabaseDriverBase db, AuthorizationService authz, IInvertedIndex search, ICollectionStore collections, string? defaultCollectionId, IGraphRepositoryFactory graphFactory, GroundedQueryService query, ModelRunnerGate gate, LoggingModule logging, AppSettings settings, ModelHealthMonitor health, ConcurrencyManager concurrency, IBlobStore? blobs = null)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (health == null) throw new ArgumentNullException(nameof(health));
            _Authz = authz;
            _Entities = new McpEntityTools(db, concurrency);
            if (blobs != null) _Entities.ContentSubmission = new ContentSubmissionService(db, blobs, settings.Ingestion);
            _GraphTools = new McpGraphTools(search, collections, defaultCollectionId, graphFactory, query);
            _Management = new McpManagementTools(db, query, logging);
            _Ops = new McpOpsTools(db, settings, health);
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
                case "pneuma_create_subject":
                    toolResult = await _Entities.CreateSubjectAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_update_subject":
                    toolResult = await _Entities.UpdateSubjectAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_jobs":
                    toolResult = await _Entities.EnumerateJobsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_job":
                    toolResult = await _Entities.GetJobAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_ingestion_summary":
                    toolResult = await _Entities.IngestionSummaryAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_submit_content":
                    toolResult = await _Entities.SubmitContentAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_crawl_plans":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.EnumeratePlansAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.GetPlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_create_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.CreatePlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_update_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.UpdatePlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_test_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.TestPlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_preview_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.PreviewPlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_start_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.StartPlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_stop_crawl_plan":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.StopPlanAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_crawl_operations":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.EnumerateOperationsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_crawl_operation":
                    if (Crawl == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Crawl plan tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Crawl.GetOperationAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_ontologies":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.EnumerateOntologiesAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_ontology_version":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.GetVersionAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_subject_ontology":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.GetSubjectOntologyAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_ontology_violations":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.EnumerateViolationsAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_start_ontology_operation":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.StartOperationAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_ontology_operation":
                    if (Ontology == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32601, "Ontology tools are not available on this server.").ConfigureAwait(false); return; }
                    toolResult = await Ontology.GetOperationAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_links":
                    toolResult = await _Entities.EnumerateLinksAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_set_link_refresh":
                    toolResult = await _Entities.SetLinkRefreshAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_link":
                    toolResult = await _Entities.GetLinkAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_history_turn":
                    toolResult = await _Entities.GetHistoryTurnAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_threads":
                    toolResult = await _Entities.EnumerateThreadsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_enumerate_feedback":
                    toolResult = await _Entities.EnumerateFeedbackAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_analytics":
                    toolResult = await _Entities.AnalyticsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_enumerate_eval_runs":
                    toolResult = await _Entities.EnumerateEvalRunsAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_eval_run":
                    toolResult = await _Entities.GetEvalRunAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_get_thread":
                    toolResult = await _Management.GetThreadAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_delete_thread":
                    toolResult = await _Management.DeleteThreadAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_eval_facts":
                    toolResult = await _Management.EnumerateEvalFactsAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_create_eval_fact":
                    toolResult = await _Management.CreateEvalFactAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_delete_eval_fact":
                    toolResult = await _Management.DeleteEvalFactAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_start_eval_run":
                    toolResult = await _Management.StartEvalRunAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_cancel_eval_run":
                    toolResult = await _Management.CancelEvalRunAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_delete_eval_run":
                    toolResult = await _Management.DeleteEvalRunAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_distinct_labels":
                    toolResult = await _Management.DistinctLabelsAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_distinct_tags":
                    toolResult = await _Management.DistinctTagsAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_enumerate_request_history":
                    toolResult = await _Ops.EnumerateRequestHistoryAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_request_history":
                    toolResult = await _Ops.GetRequestHistoryAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_request_history_summary":
                    toolResult = await _Ops.RequestHistorySummaryAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_settings":
                    toolResult = _Ops.GetSettings();
                    break;
                case "pneuma_enumerate_model_runner_health":
                    toolResult = await _Ops.EnumerateModelRunnerHealthAsync(rc, arguments, ctx.Token).ConfigureAwait(false);
                    break;
                case "pneuma_get_model_runner_health":
                    toolResult = await _Ops.GetModelRunnerHealthAsync(ctx, rc, id, arguments, ctx.Token).ConfigureAwait(false);
                    if (toolResult == null) return; // error already sent
                    break;
                case "pneuma_search":
                    toolResult = await _GraphTools.SearchAsync(rc.TenantId ?? String.Empty, arguments, null, null, ctx.Token, McpJsonRpc.FilterFromArguments(arguments)).ConfigureAwait(false);
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
