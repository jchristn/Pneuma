namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP tools over the relational entities (subjects, ingestion jobs, content links). Each collection
    /// is exposed as an <c>enumerate</c> tool returning small summary projections paged through the
    /// <see cref="EnumerationQuery"/>/<see cref="EnumerationResult{T}"/> envelope, plus a <c>get</c> tool
    /// returning one full object by id. No collection is ever returned unbounded.
    /// </summary>
    public class McpEntityTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the entity tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="db"/> is null.</exception>
        public McpEntityTools(DatabaseDriverBase db)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #endregion

        #region Public-Methods

        /// <summary>Enumerate subject summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateSubjectsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<Subject> subjects = String.IsNullOrEmpty(tenantId)
                ? new List<Subject>()
                : await _Db.Subjects.EnumerateAsync(tenantId, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<Subject> page = EnumerationHelper.Paginate(subjects, query, c => c.CreatedUtc, c => c.DisplayName);

            List<object> summaries = new List<object>();
            foreach (Subject subject in page.Objects)
            {
                summaries.Add(new { id = subject.Id, displayName = subject.DisplayName, type = subject.Type.ToString(), active = subject.Active });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Enumerate conversation threads, optionally scoped to a subject.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The threads.</returns>
        public async Task<object> EnumerateThreadsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectId = GetOptionalString(arguments, "subjectId");
            List<ChatThread> threads = String.IsNullOrEmpty(tenantId) ? new List<ChatThread>() : await _Db.ChatThreads.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return new { objects = threads };
        }

        /// <summary>Enumerate chat feedback, optionally scoped to a subject.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The feedback.</returns>
        public async Task<object> EnumerateFeedbackAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectId = GetOptionalString(arguments, "subjectId");
            List<ChatFeedback> feedback = String.IsNullOrEmpty(tenantId) ? new List<ChatFeedback>() : await _Db.ChatFeedback.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return new { objects = feedback };
        }

        /// <summary>Fetch a chat turn with its feedback, tool-call trace, and performance telemetry.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The turn detail, or null when an error response was already sent.</returns>
        public async Task<object?> GetHistoryTurnAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string turnId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(turnId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            ChatTurnRecord? turn = String.IsNullOrEmpty(tenantId) ? null : await _Db.ChatTurns.ReadAsync(tenantId, turnId, token).ConfigureAwait(false);
            if (turn == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Chat turn not found.").ConfigureAwait(false); return null; }
            List<ChatFeedback> allFeedback = await _Db.ChatFeedback.EnumerateAsync(tenantId, turn.SubjectId, token).ConfigureAwait(false);
            List<ChatFeedback> feedback = allFeedback.FindAll(f => String.Equals(f.TurnId, turn.Id, StringComparison.Ordinal));
            List<ChatToolCall> toolCalls = await _Db.ChatToolCalls.EnumerateByTurnAsync(tenantId, turn.Id, token).ConfigureAwait(false);
            return new { turn, feedback, toolCalls };
        }

        /// <summary>Per-subject chat analytics over a window.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The analytics report.</returns>
        public async Task<object> AnalyticsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectId = GetOptionalString(arguments, "subjectId");
            int days = 30;
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("days", out JsonElement d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out int parsed)) days = Math.Clamp(parsed, 1, 365);
            DateTime sinceUtc = DateTime.UtcNow.AddDays(-days);
            return await new AnalyticsService(_Db).BuildAsync(tenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, sinceUtc, token).ConfigureAwait(false);
        }

        /// <summary>Enumerate evaluation runs, optionally scoped to a subject.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The runs.</returns>
        public async Task<object> EnumerateEvalRunsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string? subjectId = GetOptionalString(arguments, "subjectId");
            List<EvalRun> runs = String.IsNullOrEmpty(tenantId) ? new List<EvalRun>() : await _Db.EvalRuns.EnumerateAsync(tenantId, subjectId, token).ConfigureAwait(false);
            return new { objects = runs };
        }

        /// <summary>Fetch an evaluation run with its results.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The run and its results, or null when an error response was already sent.</returns>
        public async Task<object?> GetEvalRunAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string runId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(runId)) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false); return null; }
            string tenantId = rc.TenantId ?? String.Empty;
            EvalRun? run = String.IsNullOrEmpty(tenantId) ? null : await _Db.EvalRuns.ReadAsync(tenantId, runId, token).ConfigureAwait(false);
            if (run == null) { await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Run not found.").ConfigureAwait(false); return null; }
            List<EvalResult> results = await _Db.EvalResults.EnumerateByRunAsync(tenantId, runId, token).ConfigureAwait(false);
            return new { run, results };
        }

        /// <summary>Fetch a single full subject by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The subject, or null when an error response was already sent.</returns>
        public async Task<object?> GetSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(subjectId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            Subject? subject = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);

            if (subject == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Subject not found.").ConfigureAwait(false);
                return null;
            }

            return subject;
        }

        /// <summary>Create a subject for the caller's tenant. Mirrors POST /v1.0/subjects (slug auto-gen; an
        /// explicit slug clash is an error).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created subject, or null when an error response was already sent.</returns>
        public async Task<object?> CreateSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            if (String.IsNullOrEmpty(tenantId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: tenant could not be resolved.").ConfigureAwait(false);
                return null;
            }
            string displayName = McpJsonRpc.GetStringArgument(arguments, "displayName");
            if (String.IsNullOrWhiteSpace(displayName))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'displayName' is required.").ConfigureAwait(false);
                return null;
            }

            Subject subject = new Subject { TenantId = tenantId, DisplayName = displayName };
            string? typeArg = GetOptionalString(arguments, "type");
            if (!String.IsNullOrWhiteSpace(typeArg)) subject.Type = typeArg!;
            subject.Description = GetOptionalString(arguments, "description");
            subject.Tagline = GetOptionalString(arguments, "tagline");
            if (String.IsNullOrWhiteSpace(subject.Tagline)) subject.Tagline = Subject.DefaultTagline;
            subject.SystemPrompt = GetOptionalString(arguments, "systemPrompt");
            subject.OntologyClassifyPrompt = GetOptionalString(arguments, "ontologyClassifyPrompt");
            subject.OntologyDefinitionPrompt = GetOptionalString(arguments, "ontologyDefinitionPrompt");
            subject.EmbeddingModel = GetOptionalString(arguments, "embeddingModel");
            subject.InferenceModel = GetOptionalString(arguments, "inferenceModel");
            subject.RerankingModel = GetOptionalString(arguments, "rerankingModel");
            subject.PromptRewriteModel = GetOptionalString(arguments, "promptRewriteModel");
            subject.Collection = GetOptionalString(arguments, "collection");
            subject.RerankingPrompt = GetOptionalString(arguments, "rerankingPrompt");
            if (String.IsNullOrWhiteSpace(subject.RerankingPrompt)) subject.RerankingPrompt = Subject.DefaultRerankingPrompt;
            subject.PromptRewritePrompt = GetOptionalString(arguments, "promptRewritePrompt");
            if (String.IsNullOrWhiteSpace(subject.PromptRewritePrompt)) subject.PromptRewritePrompt = Subject.DefaultPromptRewritePrompt;
            subject.ThinkingEnabled = GetOptionalBool(arguments, "thinkingEnabled") ?? false;
            int? retention = GetOptionalInt(arguments, "historyRetentionDays");
            if (retention.HasValue) subject.HistoryRetentionDays = retention.Value;
            subject.GraphRootNodeId = SlugHelper.Slugify(displayName);

            string? resolved = await ResolveSlugAsync(ctx, id, tenantId, GetOptionalString(arguments, "urlSlug"), displayName, null, token).ConfigureAwait(false);
            if (resolved == null) return null;
            subject.UrlSlug = resolved;

            return await _Db.Subjects.CreateAsync(subject, token).ConfigureAwait(false);
        }

        /// <summary>Update an existing subject for the caller's tenant. Only fields present in the arguments are
        /// changed; mirrors PUT /v1.0/subjects/{id} (a changed slug must stay unique).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated subject, or null when an error response was already sent.</returns>
        public async Task<object?> UpdateSubjectAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(subjectId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }
            Subject? existing = String.IsNullOrEmpty(tenantId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            if (existing == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Subject not found.").ConfigureAwait(false);
                return null;
            }

            string? displayName = GetOptionalString(arguments, "displayName");
            if (!String.IsNullOrWhiteSpace(displayName)) existing.DisplayName = displayName!;
            string? typeArg = GetOptionalString(arguments, "type");
            if (typeArg != null) existing.Type = typeArg;
            if (HasProperty(arguments, "description")) existing.Description = GetOptionalString(arguments, "description");
            if (HasProperty(arguments, "tagline")) existing.Tagline = GetOptionalString(arguments, "tagline");
            if (HasProperty(arguments, "systemPrompt")) existing.SystemPrompt = GetOptionalString(arguments, "systemPrompt");
            if (HasProperty(arguments, "ontologyClassifyPrompt")) existing.OntologyClassifyPrompt = GetOptionalString(arguments, "ontologyClassifyPrompt");
            if (HasProperty(arguments, "ontologyDefinitionPrompt")) existing.OntologyDefinitionPrompt = GetOptionalString(arguments, "ontologyDefinitionPrompt");
            if (HasProperty(arguments, "embeddingModel")) existing.EmbeddingModel = GetOptionalString(arguments, "embeddingModel");
            if (HasProperty(arguments, "inferenceModel")) existing.InferenceModel = GetOptionalString(arguments, "inferenceModel");
            if (HasProperty(arguments, "rerankingModel")) existing.RerankingModel = GetOptionalString(arguments, "rerankingModel");
            if (HasProperty(arguments, "promptRewriteModel")) existing.PromptRewriteModel = GetOptionalString(arguments, "promptRewriteModel");
            if (HasProperty(arguments, "collection")) existing.Collection = GetOptionalString(arguments, "collection");
            if (HasProperty(arguments, "rerankingPrompt")) existing.RerankingPrompt = GetOptionalString(arguments, "rerankingPrompt");
            if (HasProperty(arguments, "promptRewritePrompt")) existing.PromptRewritePrompt = GetOptionalString(arguments, "promptRewritePrompt");
            bool? thinking = GetOptionalBool(arguments, "thinkingEnabled");
            if (thinking.HasValue) existing.ThinkingEnabled = thinking.Value;
            int? retention = GetOptionalInt(arguments, "historyRetentionDays");
            if (retention.HasValue) existing.HistoryRetentionDays = retention.Value;
            bool? active = GetOptionalBool(arguments, "active");
            if (active.HasValue) existing.Active = active.Value;

            string? explicitSlug = GetOptionalString(arguments, "urlSlug");
            if (!String.IsNullOrWhiteSpace(explicitSlug))
            {
                string desired = SlugHelper.Slugify(explicitSlug);
                if (!String.Equals(desired, existing.UrlSlug, StringComparison.Ordinal))
                {
                    Subject? clash = await _Db.Subjects.ReadBySlugAsync(tenantId, desired, token).ConfigureAwait(false);
                    if (clash != null && clash.Id != existing.Id)
                    {
                        await McpJsonRpc.SendErrorAsync(ctx, id, -32009, "A subject with URL slug '" + desired + "' already exists.").ConfigureAwait(false);
                        return null;
                    }
                    existing.UrlSlug = desired;
                }
            }

            return await _Db.Subjects.UpdateAsync(existing, token).ConfigureAwait(false);
        }

        // Resolve a create-time slug: a generated slug is de-duplicated with a numeric suffix; an explicit,
        // already-taken slug is a conflict (error sent, null returned).
        private async Task<string?> ResolveSlugAsync(HttpContextBase ctx, object? id, string tenantId, string? explicitSlug, string displayName, string? ignoreSubjectId, CancellationToken token)
        {
            bool isExplicit = !String.IsNullOrWhiteSpace(explicitSlug);
            string desired = SlugHelper.Slugify(isExplicit ? explicitSlug : displayName);
            if (String.IsNullOrWhiteSpace(desired)) desired = "subject";

            Subject? clash = await _Db.Subjects.ReadBySlugAsync(tenantId, desired, token).ConfigureAwait(false);
            if (clash != null && clash.Id != ignoreSubjectId)
            {
                if (isExplicit)
                {
                    await McpJsonRpc.SendErrorAsync(ctx, id, -32009, "A subject with URL slug '" + desired + "' already exists.").ConfigureAwait(false);
                    return null;
                }
                for (int suffix = 2; suffix < 10000; suffix++)
                {
                    string candidate = desired + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Subject? existing = await _Db.Subjects.ReadBySlugAsync(tenantId, candidate, token).ConfigureAwait(false);
                    if (existing == null) return candidate;
                }
                return desired + "-" + IdGenerator.GenerateSubjectId();
            }
            return desired;
        }

        private static string? GetOptionalString(JsonElement args, string name)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.String) return e.GetString();
            return null;
        }

        private static bool? GetOptionalBool(JsonElement args, string name)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement e))
            {
                if (e.ValueKind == JsonValueKind.True) return true;
                if (e.ValueKind == JsonValueKind.False) return false;
            }
            return null;
        }

        private static int? GetOptionalInt(JsonElement args, string name)
        {
            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement e) && e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out int v)) return v;
            return null;
        }

        private static bool HasProperty(JsonElement args, string name)
        {
            return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out _);
        }

        /// <summary>Enumerate ingestion-job summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateJobsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<IngestionJob> jobs = String.IsNullOrEmpty(tenantId)
                ? new List<IngestionJob>()
                : await _Db.IngestionJobs.EnumerateAsync(tenantId, null, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<IngestionJob> page = EnumerationHelper.Paginate(jobs, query, j => j.CreatedUtc, j => j.SourceUrl);

            List<object> summaries = new List<object>();
            foreach (IngestionJob job in page.Objects)
            {
                summaries.Add(new { id = job.Id, status = job.Status.ToString(), stage = job.Stage.ToString(), sourceUrl = job.SourceUrl, documentType = job.DocumentType });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch a single full ingestion job by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job, or null when an error response was already sent.</returns>
        public async Task<object?> GetJobAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string jobId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(jobId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            IngestionJob? job = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.IngestionJobs.ReadAsync(tenantId, jobId, token).ConfigureAwait(false);

            if (job == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Ingestion job not found.").ConfigureAwait(false);
                return null;
            }

            return job;
        }

        /// <summary>
        /// Summarize ingestion activity for the caller's tenant into time buckets broken down by pipeline
        /// stage. Optional <c>subjectId</c>, <c>fromUtc</c>/<c>toUtc</c> window, and <c>bucketMinutes</c>.
        /// </summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A stage-stacked ingestion activity summary.</returns>
        public async Task<object> IngestionSummaryAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            IngestionActivityFilter filter = new IngestionActivityFilter
            {
                TenantId = String.IsNullOrEmpty(tenantId) ? null : tenantId
            };

            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            if (!String.IsNullOrEmpty(subjectId)) filter.SubjectId = subjectId;

            filter.FromUtc = ParseUtcArgument(arguments, "fromUtc");
            filter.ToUtc = ParseUtcArgument(arguments, "toUtc");

            if (arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty("bucketMinutes", out JsonElement bmEl)
                && bmEl.ValueKind == JsonValueKind.Number
                && bmEl.TryGetInt32(out int bucketMinutes))
            {
                filter.BucketMinutes = bucketMinutes;
            }

            return await _Db.IngestionJobEvents.SummarizeAsync(filter, token).ConfigureAwait(false);
        }

        private static DateTime? ParseUtcArgument(JsonElement arguments, string name)
        {
            string value = McpJsonRpc.GetStringArgument(arguments, name);
            if (String.IsNullOrEmpty(value)) return null;
            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed))
            {
                return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
            }
            return null;
        }

        /// <summary>Enumerate content-link summaries for the caller's tenant, paged.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>An enumeration-result payload.</returns>
        public async Task<object> EnumerateLinksAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            List<SubjectLink> links = String.IsNullOrEmpty(tenantId)
                ? new List<SubjectLink>()
                : await _Db.SubjectLinks.EnumerateAsync(tenantId, token).ConfigureAwait(false);

            EnumerationQuery query = McpJsonRpc.QueryFromArguments(arguments);
            EnumerationResult<SubjectLink> page = EnumerationHelper.Paginate(links, query, l => l.CreatedUtc, l => l.Url);

            List<object> summaries = new List<object>();
            foreach (SubjectLink link in page.Objects)
            {
                summaries.Add(new { id = link.Id, url = link.Url, title = link.Title, status = link.Status.ToString(), subjectId = link.SubjectId });
            }

            return BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch a single full content link by id.</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The link, or null when an error response was already sent.</returns>
        public async Task<object?> GetLinkAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string linkId = McpJsonRpc.GetStringArgument(arguments, "id");
            if (String.IsNullOrEmpty(linkId))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'id' is required.").ConfigureAwait(false);
                return null;
            }

            string tenantId = rc.TenantId ?? String.Empty;
            SubjectLink? link = String.IsNullOrEmpty(tenantId)
                ? null
                : await _Db.SubjectLinks.ReadAsync(tenantId, linkId, token).ConfigureAwait(false);

            if (link == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Content link not found.").ConfigureAwait(false);
                return null;
            }

            return link;
        }

        #endregion

        #region Private-Methods

        private static object BuildPage(int maxResults, int skip, long totalRecords, long recordsRemaining, bool endOfResults, List<object> objects)
        {
            return new
            {
                success = true,
                maxResults,
                skip,
                totalRecords,
                recordsRemaining,
                endOfResults,
                objects
            };
        }

        #endregion
    }
}
