namespace Pneuma.Server.Mcp
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Crawling;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// MCP tools for crawl plans and crawl operations. They share the services the REST routes use, so validation,
    /// secret handling, and start and stop behave the same. Secrets are write-only here too.
    /// </summary>
    public class McpCrawlTools
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly CrawlerFactory _Crawlers;
        private readonly CrawlPlanService _Plans;
        private readonly CrawlSyncService _Sync;
        private readonly CrawlSchedulerService _Scheduler;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the tools.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="crawlers">Registered crawlers.</param>
        /// <param name="plans">Plan service.</param>
        /// <param name="sync">Sync service.</param>
        /// <param name="scheduler">Scheduler.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public McpCrawlTools(DatabaseDriverBase db, CrawlerFactory crawlers, CrawlPlanService plans, CrawlSyncService sync, CrawlSchedulerService scheduler)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Crawlers = crawlers ?? throw new ArgumentNullException(nameof(crawlers));
            _Plans = plans ?? throw new ArgumentNullException(nameof(plans));
            _Sync = sync ?? throw new ArgumentNullException(nameof(sync));
            _Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        }

        #endregion

        #region Public-Methods

        /// <summary>Enumerate crawl plans as summaries (<c>pneuma_enumerate_crawl_plans</c>), optionally for one subject.</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of summaries.</returns>
        public async Task<object> EnumeratePlansAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            List<CrawlPlan> plans = String.IsNullOrEmpty(tenantId)
                ? new List<CrawlPlan>()
                : String.IsNullOrEmpty(subjectId)
                    ? await _Db.CrawlPlans.EnumerateAsync(tenantId, token).ConfigureAwait(false)
                    : await _Db.CrawlPlans.EnumerateBySubjectAsync(tenantId, subjectId, token).ConfigureAwait(false);
            EnumerationResult<CrawlPlan> page = EnumerationHelper.Paginate(plans, McpJsonRpc.QueryFromArguments(arguments), p => p.CreatedUtc, p => p.Name);
            List<object> summaries = page.Objects.Select(p => (object)new
            {
                id = p.Id,
                name = p.Name,
                type = p.Type.ToString(),
                subjectId = p.SubjectId,
                enabled = p.Enabled,
                status = p.Status.ToString(),
                schedule = p.Schedule.Type.ToString(),
                lastRunUtc = p.LastRunUtc,
                nextRunUtc = p.NextRunUtc
            }).ToList();
            return McpJsonRpc.BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch one crawl plan (<c>pneuma_get_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The plan, or null when an error response was already sent.</returns>
        public async Task<object?> GetPlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            return await ReadPlanOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
        }

        /// <summary>Create a crawl plan (<c>pneuma_create_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: subjectId and plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created plan, or null when an error response was already sent.</returns>
        public async Task<object?> CreatePlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string subjectId = McpJsonRpc.GetStringArgument(arguments, "subjectId");
            Subject? subject = String.IsNullOrEmpty(tenantId) || String.IsNullOrEmpty(subjectId) ? null : await _Db.Subjects.ReadAsync(tenantId, subjectId, token).ConfigureAwait(false);
            if (subject == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Subject not found.").ConfigureAwait(false);
                return null;
            }
            if (String.IsNullOrEmpty(subject.EmbeddingModel) || String.IsNullOrEmpty(subject.InferenceModel) || String.IsNullOrEmpty(subject.Collection))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "The subject needs an embedding model, an inference model, and a collection before it can be crawled into.").ConfigureAwait(false);
                return null;
            }

            CrawlPlanRequest? request = await ReadRequestOrErrorAsync(ctx, id, arguments).ConfigureAwait(false);
            if (request == null) return null;
            CrawlPlan plan = request.ToPlan(tenantId, subjectId);
            CrawlPlanService.Clean(plan);
            List<string> errors = _Plans.Validate(plan, null);
            if (errors.Count > 0)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: " + String.Join(" ", errors)).ConfigureAwait(false);
                return null;
            }
            if (CrawlPlanService.TurnsOffRobots(null, plan) && !rc.IsAdmin && !rc.IsTenantAdmin)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32000, "Only an administrator can turn off robots.txt for a crawl plan.").ConfigureAwait(false);
                return null;
            }
            CrawlPlanSaveResult result = await _Plans.CreateAsync(plan, token).ConfigureAwait(false);
            await AuditSecretsAsync(rc, result, token).ConfigureAwait(false);
            return result.Plan;
        }

        /// <summary>Replace a crawl plan's configuration (<c>pneuma_update_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id and plan.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated plan, or null when an error response was already sent.</returns>
        public async Task<object?> UpdatePlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            CrawlPlan? existing = await ReadPlanOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (existing == null) return null;
            CrawlPlanRequest? request = await ReadRequestOrErrorAsync(ctx, id, arguments).ConfigureAwait(false);
            if (request == null) return null;
            if (request.Type != existing.Type)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: a crawl plan's type cannot change.").ConfigureAwait(false);
                return null;
            }
            CrawlPlan incoming = request.ToPlan(existing.TenantId, existing.SubjectId);
            CrawlPlanService.Clean(incoming);
            List<string> remaining = existing.SecretsSet.Where(s => request.ClearSecrets == null || !request.ClearSecrets.Contains(s, StringComparer.Ordinal)).ToList();
            List<string> errors = _Plans.Validate(incoming, remaining);
            if (errors.Count > 0)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: " + String.Join(" ", errors)).ConfigureAwait(false);
                return null;
            }
            if (CrawlPlanService.TurnsOffRobots(existing, incoming) && !rc.IsAdmin && !rc.IsTenantAdmin)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32000, "Only an administrator can turn off robots.txt for a crawl plan.").ConfigureAwait(false);
                return null;
            }
            CrawlPlanSaveResult result = await _Plans.UpdateAsync(existing, incoming, request.ClearSecrets, token).ConfigureAwait(false);
            await AuditSecretsAsync(rc, result, token).ConfigureAwait(false);
            return result.Plan;
        }

        /// <summary>Test a crawl plan's connection (<c>pneuma_test_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The connectivity result, or null when an error response was already sent.</returns>
        public async Task<object?> TestPlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            CrawlPlan? plan = await ReadPlanWithSecretsOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (plan == null) return null;
            return await _Crawlers.Get(plan.Type).TestAsync(plan, token).ConfigureAwait(false);
        }

        /// <summary>Preview a crawl plan (<c>pneuma_preview_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The preview, or null when an error response was already sent.</returns>
        public async Task<object?> PreviewPlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            CrawlPlan? plan = await ReadPlanWithSecretsOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (plan == null) return null;
            try
            {
                return await _Sync.PreviewAsync(plan, true, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32603, "The source could not be listed: " + e.Message).ConfigureAwait(false);
                return null;
            }
        }

        /// <summary>Start a crawl operation (<c>pneuma_start_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The started operation, or null when an error response was already sent.</returns>
        public async Task<object?> StartPlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            CrawlPlan? plan = await ReadPlanOrErrorAsync(ctx, rc, id, arguments, token).ConfigureAwait(false);
            if (plan == null) return null;
            if (!_Crawlers.IsAvailable(plan.Type))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "No crawler is available for " + plan.Type + " plans on this server.").ConfigureAwait(false);
                return null;
            }
            CrawlStartResult result = await _Scheduler.StartAsync(plan.TenantId, plan.Id, CrawlTriggerEnum.Manual, token).ConfigureAwait(false);
            return await ResultOrErrorAsync(ctx, id, result).ConfigureAwait(false);
        }

        /// <summary>Stop a crawl plan's operation (<c>pneuma_stop_crawl_plan</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stopped operation, or null when an error response was already sent.</returns>
        public async Task<object?> StopPlanAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string planId = McpJsonRpc.GetStringArgument(arguments, "id");
            CrawlStartResult result = await _Scheduler.StopAsync(rc.TenantId ?? String.Empty, planId, token).ConfigureAwait(false);
            return await ResultOrErrorAsync(ctx, id, result).ConfigureAwait(false);
        }

        /// <summary>Enumerate crawl operations as summaries (<c>pneuma_enumerate_crawl_operations</c>).</summary>
        /// <param name="rc">Request context.</param>
        /// <param name="arguments">Tool arguments: optional planId.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of summaries.</returns>
        public async Task<object> EnumerateOperationsAsync(RequestContext rc, JsonElement arguments, CancellationToken token)
        {
            string tenantId = rc.TenantId ?? String.Empty;
            string planId = McpJsonRpc.GetStringArgument(arguments, "planId");
            List<CrawlOperation> operations = String.IsNullOrEmpty(tenantId)
                ? new List<CrawlOperation>()
                : String.IsNullOrEmpty(planId)
                    ? await _Db.CrawlOperations.EnumerateAsync(tenantId, token).ConfigureAwait(false)
                    : await _Db.CrawlOperations.EnumerateByPlanAsync(tenantId, planId, token).ConfigureAwait(false);
            EnumerationResult<CrawlOperation> page = EnumerationHelper.Paginate(operations, McpJsonRpc.QueryFromArguments(arguments), o => o.StartedUtc, o => o.PlanId);
            List<object> summaries = page.Objects.Select(o => (object)new
            {
                id = o.Id,
                planId = o.PlanId,
                status = o.Status.ToString(),
                trigger = o.Trigger.ToString(),
                added = o.Added,
                updated = o.Updated,
                deleted = o.Deleted,
                failed = o.Failed,
                startedUtc = o.StartedUtc,
                finishedUtc = o.FinishedUtc
            }).ToList();
            return McpJsonRpc.BuildPage(page.MaxResults, page.Skip, page.TotalRecords, page.RecordsRemaining, page.EndOfResults, summaries);
        }

        /// <summary>Fetch one crawl operation (<c>pneuma_get_crawl_operation</c>).</summary>
        /// <param name="ctx">HTTP context (for error responses).</param>
        /// <param name="rc">Request context.</param>
        /// <param name="id">JSON-RPC request id.</param>
        /// <param name="arguments">Tool arguments: id.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The operation, or null when an error response was already sent.</returns>
        public async Task<object?> GetOperationAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string operationId = McpJsonRpc.GetStringArgument(arguments, "id");
            CrawlOperation? operation = String.IsNullOrEmpty(operationId) ? null : await _Db.CrawlOperations.ReadAsync(rc.TenantId ?? String.Empty, operationId, token).ConfigureAwait(false);
            if (operation == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Crawl operation not found.").ConfigureAwait(false);
                return null;
            }
            return operation;
        }

        #endregion

        #region Private-Methods

        private async Task<CrawlPlan?> ReadPlanOrErrorAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string planId = McpJsonRpc.GetStringArgument(arguments, "id");
            CrawlPlan? plan = String.IsNullOrEmpty(planId) ? null : await _Db.CrawlPlans.ReadAsync(rc.TenantId ?? String.Empty, planId, token).ConfigureAwait(false);
            if (plan == null) await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Crawl plan not found.").ConfigureAwait(false);
            return plan;
        }

        private async Task<CrawlPlan?> ReadPlanWithSecretsOrErrorAsync(HttpContextBase ctx, RequestContext rc, object? id, JsonElement arguments, CancellationToken token)
        {
            string planId = McpJsonRpc.GetStringArgument(arguments, "id");
            CrawlPlan? plan = String.IsNullOrEmpty(planId) ? null : await _Plans.ReadWithSecretsAsync(rc.TenantId ?? String.Empty, planId, token).ConfigureAwait(false);
            if (plan == null)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32004, "Crawl plan not found.").ConfigureAwait(false);
                return null;
            }
            if (!_Crawlers.IsAvailable(plan.Type))
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "No crawler is available for " + plan.Type + " plans on this server.").ConfigureAwait(false);
                return null;
            }
            return plan;
        }

        private static async Task<CrawlPlanRequest?> ReadRequestOrErrorAsync(HttpContextBase ctx, object? id, JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("plan", out JsonElement planElement) || planElement.ValueKind != JsonValueKind.Object)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'plan' (an object) is required.").ConfigureAwait(false);
                return null;
            }
            try
            {
                CrawlPlanRequest? request = Json.Deserialize<CrawlPlanRequest>(planElement.GetRawText());
                if (request == null) await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'plan' is empty.").ConfigureAwait(false);
                return request;
            }
            catch (Exception e)
            {
                await McpJsonRpc.SendErrorAsync(ctx, id, -32602, "Invalid params: 'plan' could not be read: " + e.Message).ConfigureAwait(false);
                return null;
            }
        }

        private static async Task<object?> ResultOrErrorAsync(HttpContextBase ctx, object? id, CrawlStartResult result)
        {
            if (result.StatusCode >= 400)
            {
                int code = result.StatusCode == 404 ? -32004 : result.StatusCode == 409 ? -32009 : -32602;
                await McpJsonRpc.SendErrorAsync(ctx, id, code, result.Error ?? "Request refused.").ConfigureAwait(false);
                return null;
            }
            return result.Operation;
        }

        private async Task AuditSecretsAsync(RequestContext rc, CrawlPlanSaveResult result, CancellationToken token)
        {
            if (result.ChangedSecrets.Count == 0) return;
            try
            {
                AuditRecord record = new AuditRecord
                {
                    EventType = AuditEventTypeEnum.CrawlPlanSecurityChanged,
                    TenantId = rc.TenantId,
                    UserId = rc.UserId,
                    ResourceId = result.Plan.Id,
                    RequiredResourceType = ResourceTypeEnum.CrawlPlan,
                    RequiredOperation = OperationTypeEnum.Write,
                    HttpMethod = "POST",
                    UrlPath = "/mcp",
                    DenialReason = "secrets changed: " + String.Join(", ", result.ChangedSecrets)
                };
                await _Db.Audit.CreateAsync(record, token).ConfigureAwait(false);
            }
            catch (Exception e) when (!(e is OperationCanceledException))
            {
                // Audit is best-effort; the change stands either way.
            }
        }

        #endregion
    }
}
