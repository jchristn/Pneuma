namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP management tools that mirror the REST write/management surface for conversation threads, the RAG
    /// evaluation harness, and retrieval-facet discovery. Kept separate from the read-only entity/graph tools so
    /// each file stays cohesive; authorization is applied by <see cref="McpToolAuthorization"/> exactly as the
    /// REST twins gate, so the two paths cannot drift.
    /// </summary>
    public class McpManagementTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly EvalService _Eval;
        private readonly FacetDiscoveryService _Facets;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the management tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="query">Grounded query service (used by the eval harness to answer and judge facts).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public McpManagementTools(DatabaseDriverBase db, GroundedQueryService query, LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Eval = new EvalService(db, query, logging);
            _Facets = new FacetDiscoveryService(db);
        }

        #endregion

        #region Public-Methods

        /// <summary>Fetch one conversation thread with its turns.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread and its turns, or null when an error response was already sent.</returns>
        public async Task<object?> GetThreadAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string threadId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(threadId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            ChatThread? thread = String.IsNullOrEmpty(tenantId) ? null : await _Db.ChatThreads.ReadAsync(tenantId, threadId, token).ConfigureAwait(false);
            if (thread == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Thread not found.").ConfigureAwait(false); return null; }
            List<ChatTurnRecord> turns = await _Db.ChatTurns.EnumerateByThreadAsync(tenantId, threadId, token).ConfigureAwait(false);
            return new { thread, turns };
        }

        /// <summary>Delete a conversation thread and cascade its turns and tool calls.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A deletion acknowledgement, or null when an error response was already sent.</returns>
        public async Task<object?> DeleteThreadAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string threadId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(threadId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            ChatThread? thread = String.IsNullOrEmpty(tenantId) ? null : await _Db.ChatThreads.ReadAsync(tenantId, threadId, token).ConfigureAwait(false);
            if (thread == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Thread not found.").ConfigureAwait(false); return null; }

            List<ChatTurnRecord> turns = await _Db.ChatTurns.EnumerateByThreadAsync(tenantId, threadId, token).ConfigureAwait(false);
            foreach (ChatTurnRecord turn in turns)
            {
                await _Db.ChatToolCalls.DeleteByTurnAsync(tenantId, turn.Id, token).ConfigureAwait(false);
            }
            await _Db.ChatTurns.DeleteByThreadAsync(tenantId, threadId, token).ConfigureAwait(false);
            await _Db.ChatThreads.DeleteAsync(tenantId, threadId, token).ConfigureAwait(false);
            return new { deleted = true, id = threadId };
        }

        /// <summary>Enumerate a subject's evaluation facts as small summaries, paged.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload, or null when an error response was already sent.</returns>
        public async Task<object?> EnumerateEvalFactsAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            if (String.IsNullOrEmpty(subjectId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'subjectId' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            List<EvalFact> facts = String.IsNullOrEmpty(tenantId) ? new List<EvalFact>() : await _Db.EvalFacts.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<EvalFact> page = EnumerationHelper.Paginate(facts, query, f => f.CreatedUtc, f => f.Question);

            List<object> summaries = new List<object>();
            foreach (EvalFact fact in page.Objects)
            {
                summaries.Add(new { id = fact.Id, subjectId = fact.SubjectId, question = fact.Question, category = fact.Category, createdUtc = fact.CreatedUtc });
            }

            return McpJsonRpc.BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Create an evaluation fact for a subject.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created fact, or null when an error response was already sent.</returns>
        public async Task<object?> CreateEvalFactAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: tenant could not be resolved.").ConfigureAwait(false); return null; }
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            string question = McpJsonRpc.GetStringArgument(arguments, "question");
            string expectedAnswer = McpJsonRpc.GetStringArgument(arguments, "expectedAnswer");
            if (String.IsNullOrWhiteSpace(subjectId) || String.IsNullOrWhiteSpace(question) || String.IsNullOrWhiteSpace(expectedAnswer))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'subjectId', 'question', and 'expectedAnswer' are required.").ConfigureAwait(false);
                return null;
            }
            EvalFact fact = new EvalFact { TenantId = tenantId, SubjectId = subjectId, Question = question, ExpectedAnswer = expectedAnswer, Category = GetOptionalString(arguments, "category") };
            return await _Db.EvalFacts.CreateAsync(fact, token).ConfigureAwait(false);
        }

        /// <summary>Delete an evaluation fact by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A deletion acknowledgement, or null when an error response was already sent.</returns>
        public async Task<object?> DeleteEvalFactAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string factId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(factId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: tenant could not be resolved.").ConfigureAwait(false); return null; }
            await _Db.EvalFacts.DeleteAsync(tenantId, factId, token).ConfigureAwait(false);
            return new { deleted = true, id = factId };
        }

        /// <summary>Queue an evaluation run for a subject; the background worker processes it.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The queued (Pending) run, or null when an error response was already sent.</returns>
        public async Task<object?> StartEvalRunAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: tenant could not be resolved.").ConfigureAwait(false); return null; }
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            if (String.IsNullOrWhiteSpace(subjectId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'subjectId' is required.").ConfigureAwait(false); return null; }
            return await _Eval.CreateRunAsync(tenantId, subjectId, GetOptionalString(arguments, "category"), token).ConfigureAwait(false);
        }

        /// <summary>Cancel a queued or running evaluation run (idempotent; observed between facts by the worker).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run in its current state, or null when an error response was already sent.</returns>
        public async Task<object?> CancelEvalRunAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string runId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(runId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            EvalRun? run = String.IsNullOrEmpty(tenantId) ? null : await _Db.EvalRuns.ReadAsync(tenantId, runId, token).ConfigureAwait(false);
            if (run == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Run not found.").ConfigureAwait(false); return null; }

            if (run.Status == EvalRunStatusEnum.Pending || run.Status == EvalRunStatusEnum.Running)
            {
                run.Status = EvalRunStatusEnum.Cancelled;
                run.FinishedUtc = DateTime.UtcNow;
                await _Db.EvalRuns.UpdateAsync(run, token).ConfigureAwait(false);
            }
            return run;
        }

        /// <summary>Delete an evaluation run and its results.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A deletion acknowledgement, or null when an error response was already sent.</returns>
        public async Task<object?> DeleteEvalRunAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string runId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(runId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: tenant could not be resolved.").ConfigureAwait(false); return null; }
            await _Db.EvalResults.DeleteByRunAsync(tenantId, runId, token).ConfigureAwait(false);
            await _Db.EvalRuns.DeleteAsync(tenantId, runId, token).ConfigureAwait(false);
            return new { deleted = true, id = runId };
        }

        /// <summary>Return the distinct retrieval labels applied to a subject's content (a bounded aggregate).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The distinct labels, or null when an error response was already sent.</returns>
        public async Task<object?> DistinctLabelsAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            if (String.IsNullOrEmpty(subjectId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'subjectId' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            List<string> labels = String.IsNullOrEmpty(tenantId) ? new List<string>() : await _Facets.DistinctLabelsAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return new { subjectId, labels };
        }

        /// <summary>Return the distinct retrieval tag keys and values applied to a subject's content (a bounded aggregate).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The distinct tags, or null when an error response was already sent.</returns>
        public async Task<object?> DistinctTagsAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            if (String.IsNullOrEmpty(subjectId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'subjectId' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            Dictionary<string, List<string>> tags = String.IsNullOrEmpty(tenantId) ? new Dictionary<string, List<string>>() : await _Facets.DistinctTagsAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return new { subjectId, tags };
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
