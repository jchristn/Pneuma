namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;

    /// <summary>
    /// Executes Pneuma's read tools in-process without any HTTP/JSON-RPC transport, returning a
    /// <see cref="ToolInvocationResult"/> instead of writing to a response. This lets the agentic chat
    /// assistant call the same tools an external MCP client would. Enumeration and search reuse the exact
    /// <see cref="McpEntityTools"/>/<see cref="McpGraphTools"/> logic behind the MCP endpoint; single-object
    /// fetches read the same repositories directly so results cannot diverge from the MCP surface.
    /// </summary>
    public class PneumaToolExecutor
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly IGraphRepositoryFactory _GraphFactory;
        private readonly GroundedQueryService _Query;
        private readonly McpEntityTools _Entities;
        private readonly McpGraphTools _GraphTools;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tool executor.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="search">Full-text search client (RecallDB).</param>
        /// <param name="collections">Collection store used to resolve the target collection.</param>
        /// <param name="defaultCollectionId">Default collection id used when a request specifies none.</param>
        /// <param name="graphFactory">Per-tenant graph repository factory.</param>
        /// <param name="query">Shared grounded query service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public PneumaToolExecutor(DatabaseDriverBase db, AuthorizationService authz, IInvertedIndex search, ICollectionStore collections, string? defaultCollectionId, IGraphRepositoryFactory graphFactory, GroundedQueryService query)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (search == null) throw new ArgumentNullException(nameof(search));
            if (collections == null) throw new ArgumentNullException(nameof(collections));
            if (graphFactory == null) throw new ArgumentNullException(nameof(graphFactory));
            if (query == null) throw new ArgumentNullException(nameof(query));
            _Db = db;
            _Authz = authz;
            _GraphFactory = graphFactory;
            _Query = query;
            _Entities = new McpEntityTools(db);
            _GraphTools = new McpGraphTools(search, collections, defaultCollectionId, graphFactory, query);
        }

        #endregion

        #region Public-Methods

        /// <summary>Authorize and execute a tool by name, returning its result or an error.</summary>
        /// <param name="rc">Request context of the calling principal.</param>
        /// <param name="toolName">The tool name (e.g. "pneuma_search").</param>
        /// <param name="arguments">The tool arguments object.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The tool result or a failure with a message.</returns>
        public async Task<ToolInvocationResult> ExecuteAsync(RequestContext rc, string toolName, JsonElement arguments, CancellationToken token)
        {
            if (String.IsNullOrEmpty(toolName)) return ToolInvocationResult.Fail("A tool name is required.");

            if (!await McpToolAuthorization.AuthorizeAsync(_Authz, rc, toolName, token).ConfigureAwait(false))
            {
                return ToolInvocationResult.Fail("Not permitted.");
            }

            string tenantId = rc.TenantId ?? String.Empty;

            switch (toolName)
            {
                case "pneuma_capabilities":
                    return ToolInvocationResult.Ok(McpToolCatalog.BuildCapabilities());

                case "pneuma_enumerate_subjects":
                    return ToolInvocationResult.Ok(await _Entities.EnumerateSubjectsAsync(rc, arguments, token).ConfigureAwait(false));

                case "pneuma_enumerate_jobs":
                    return ToolInvocationResult.Ok(await _Entities.EnumerateJobsAsync(rc, arguments, token).ConfigureAwait(false));

                case "pneuma_enumerate_links":
                    return ToolInvocationResult.Ok(await _Entities.EnumerateLinksAsync(rc, arguments, token).ConfigureAwait(false));

                case "pneuma_search":
                    return ToolInvocationResult.Ok(await _GraphTools.SearchAsync(tenantId, arguments, token).ConfigureAwait(false));

                case "pneuma_get_subject":
                    return await GetSubjectAsync(tenantId, arguments, token).ConfigureAwait(false);

                case "pneuma_get_job":
                    return await GetJobAsync(tenantId, arguments, token).ConfigureAwait(false);

                case "pneuma_get_link":
                    return await GetLinkAsync(tenantId, arguments, token).ConfigureAwait(false);

                case "pneuma_get_node":
                    return await GetNodeAsync(tenantId, arguments, token).ConfigureAwait(false);

                case "pneuma_get_neighbors":
                    return await GetNeighborsAsync(tenantId, arguments, token).ConfigureAwait(false);

                case "pneuma_query":
                    return await GroundedQueryAsync(tenantId, arguments, token).ConfigureAwait(false);

                default:
                    return ToolInvocationResult.Fail("Unknown tool: " + toolName);
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ToolInvocationResult> GetSubjectAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string id = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(id)) return ToolInvocationResult.Fail("'id' is required.");
            Subject? subject = String.IsNullOrEmpty(tenantId) ? null : await _Db.Subjects.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (subject == null) return ToolInvocationResult.Fail("Subject not found.");
            return ToolInvocationResult.Ok(subject);
        }

        private async Task<ToolInvocationResult> GetJobAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string id = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(id)) return ToolInvocationResult.Fail("'id' is required.");
            IngestionJob? job = String.IsNullOrEmpty(tenantId) ? null : await _Db.IngestionJobs.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (job == null) return ToolInvocationResult.Fail("Ingestion job not found.");
            return ToolInvocationResult.Ok(job);
        }

        private async Task<ToolInvocationResult> GetLinkAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string id = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(id)) return ToolInvocationResult.Fail("'id' is required.");
            SubjectLink? link = String.IsNullOrEmpty(tenantId) ? null : await _Db.SubjectLinks.ReadAsync(tenantId, id, token).ConfigureAwait(false);
            if (link == null) return ToolInvocationResult.Fail("Content link not found.");
            return ToolInvocationResult.Ok(link);
        }

        private async Task<ToolInvocationResult> GetNodeAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string id = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(id)) return ToolInvocationResult.Fail("'id' is required.");
            GraphNode? node = await (await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false)).ReadNodeAsync(id, token).ConfigureAwait(false);
            if (node == null) return ToolInvocationResult.Fail("Graph node not found.");
            return ToolInvocationResult.Ok(node);
        }

        private async Task<ToolInvocationResult> GetNeighborsAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string id = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(id)) return ToolInvocationResult.Fail("'id' is required.");

            List<GraphNode> neighbors = await (await _GraphFactory.ForTenantAsync(tenantId, token).ConfigureAwait(false)).GetNeighborsAsync(id, token).ConfigureAwait(false);
            List<object> summaries = new List<object>();
            foreach (GraphNode neighbor in neighbors)
            {
                summaries.Add(new { id = neighbor.Id, name = neighbor.Name, nodeType = neighbor.NodeType });
                if (summaries.Count >= 100) break;
            }
            return ToolInvocationResult.Ok(new { nodeId = id, count = summaries.Count, neighbors = summaries });
        }

        private async Task<ToolInvocationResult> GroundedQueryAsync(string tenantId, JsonElement arguments, CancellationToken token)
        {
            string question = McpJsonRpc.GetStringArgument(arguments, "question");
            if (String.IsNullOrWhiteSpace(question)) return ToolInvocationResult.Fail("'question' is required.");

            int max = 10;
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("max", out JsonElement maxEl) && maxEl.ValueKind == JsonValueKind.Number && maxEl.TryGetInt32(out int parsed))
            {
                max = Math.Clamp(parsed, 1, 20);
            }

            GroundedAnswer answer = await _Query.AnswerAsync(tenantId, question, max, token).ConfigureAwait(false);
            List<object> sources = new List<object>();
            foreach (GraphNode source in answer.Sources)
            {
                sources.Add(new { id = source.Id, name = source.Name, nodeType = source.NodeType });
            }
            return ToolInvocationResult.Ok(new { answer = answer.Answer, grounded = answer.Grounded, insufficientSupport = answer.InsufficientSupport, sources });
        }

        #endregion
    }
}
