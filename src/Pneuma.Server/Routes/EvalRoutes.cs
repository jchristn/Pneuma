namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Models;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Server.Streaming;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// RAG evaluation routes: manage ground-truth facts for a subject, start a run (answers each fact through
    /// the real pipeline and LLM-judges it), and list/inspect/delete runs and their results.
    /// </summary>
    public class EvalRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;
        private readonly EvalService _Eval;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate evaluation routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <param name="query">Grounded query service used to answer and judge facts.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public EvalRoutes(DatabaseDriverBase db, AuthorizationService authz, GroundedQueryService query, LoggingModule logging)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Eval = new EvalService(db, query, logging);
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/eval/facts", ListFactsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List evaluation facts", "Eval"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/eval/facts", CreateFactAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create an evaluation fact", "Eval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/eval/facts/{id}", DeleteFactAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete an evaluation fact", "Eval"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/eval/runs", ListRunsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List evaluation runs", "Eval"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/eval/runs", StartRunAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Queue an evaluation run (processed by a background worker)", "Eval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/eval/runs/{id}", RunDetailAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get an evaluation run and its results", "Eval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/eval/runs/{id}/stream", StreamRunAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Stream an evaluation run's live progress (SSE)", "Eval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.POST, "/v1.0/eval/runs/{id}/cancel", CancelRunAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Cancel a queued or running evaluation run", "Eval"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/eval/runs/{id}", DeleteRunAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete an evaluation run", "Eval"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListFactsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            if (String.IsNullOrEmpty(subjectId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "subjectId is required.").ConfigureAwait(false); return; }
            List<EvalFact> facts = await _Db.EvalFacts.EnumerateBySubjectAsync(rc.TenantId, subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { objects = facts }).ConfigureAwait(false);
        }

        private async Task CreateFactAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            EvalFact? body = RouteHelper.ReadBody<EvalFact>(ctx);
            if (body == null || String.IsNullOrWhiteSpace(body.SubjectId) || String.IsNullOrWhiteSpace(body.Question) || String.IsNullOrWhiteSpace(body.ExpectedAnswer))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "subjectId, question, and expectedAnswer are required.").ConfigureAwait(false);
                return;
            }
            EvalFact fact = new EvalFact { TenantId = rc.TenantId, SubjectId = body.SubjectId, Question = body.Question, ExpectedAnswer = body.ExpectedAnswer, Category = body.Category };
            EvalFact created = await _Db.EvalFacts.CreateAsync(fact, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task DeleteFactAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            await _Db.EvalFacts.DeleteAsync(rc.TenantId, RouteHelper.Param(ctx, "id"), ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }

        private async Task ListRunsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            List<EvalRun> runs = await _Db.EvalRuns.EnumerateAsync(rc.TenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { objects = runs }).ConfigureAwait(false);
        }

        private async Task StartRunAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            EvalRun? body = RouteHelper.ReadBody<EvalRun>(ctx);
            if (body == null || String.IsNullOrWhiteSpace(body.SubjectId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "subjectId is required.").ConfigureAwait(false);
                return;
            }
            // Queue the run and return immediately; the background eval worker claims and processes it. The
            // client watches progress via GET /v1.0/eval/runs/{id}/stream.
            EvalRun run = await _Eval.CreateRunAsync(rc.TenantId, body.SubjectId, body.Category, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, run).ConfigureAwait(false);
        }

        private async Task CancelRunAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string id = RouteHelper.Param(ctx, "id");
            EvalRun? run = await _Db.EvalRuns.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (run == null) { await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Run not found.").ConfigureAwait(false); return; }

            // Cancel only a not-yet-finished run; the worker observes the status between facts and stops. This is
            // idempotent — cancelling an already-terminal run just returns its current state.
            if (run.Status == EvalRunStatusEnum.Pending || run.Status == EvalRunStatusEnum.Running)
            {
                run.Status = EvalRunStatusEnum.Cancelled;
                run.FinishedUtc = DateTime.UtcNow;
                await _Db.EvalRuns.UpdateAsync(run, ctx.Token).ConfigureAwait(false);
            }
            await RouteHelper.SendJsonAsync(ctx, 200, run).ConfigureAwait(false);
        }

        private async Task StreamRunAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string id = RouteHelper.Param(ctx, "id");
            EvalRun? initial = await _Db.EvalRuns.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (initial == null) { await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Run not found.").ConfigureAwait(false); return; }

            // Poll-based SSE: the background worker writes per-fact results and running tallies to the DB; this
            // stream observes them and pushes a metadata frame, a `result` frame per newly-judged fact, a
            // `progress` frame each tick, and a final `complete` frame when the run reaches a terminal status.
            SseWriter sse = new SseWriter(ctx);
            try
            {
                await sse.SendAsync(new { type = "metadata", runId = initial.Id, total = initial.TotalFacts, status = initial.Status.ToString() }, false, ctx.Token).ConfigureAwait(false);
                int emitted = 0;
                while (true)
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    EvalRun? current = await _Db.EvalRuns.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
                    if (current == null)
                    {
                        await sse.SendAsync(new { type = "error", message = "Run not found." }, true, ctx.Token).ConfigureAwait(false);
                        return;
                    }

                    List<EvalResult> results = await _Db.EvalResults.EnumerateByRunAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
                    for (int i = emitted; i < results.Count; i++)
                    {
                        EvalResult r = results[i];
                        await sse.SendAsync(new { type = "result", factId = r.FactId, question = r.Question, verdict = r.Verdict.ToString(), score = r.Score, category = r.Category, failureMode = r.FailureMode }, false, ctx.Token).ConfigureAwait(false);
                    }
                    emitted = results.Count;

                    await sse.SendAsync(new { type = "progress", completed = results.Count, total = current.TotalFacts, pass = current.PassCount, partial = current.PartialCount, fail = current.FailCount, status = current.Status.ToString() }, false, ctx.Token).ConfigureAwait(false);

                    bool terminal = current.Status == EvalRunStatusEnum.Completed || current.Status == EvalRunStatusEnum.Failed || current.Status == EvalRunStatusEnum.Cancelled;
                    if (terminal)
                    {
                        await sse.SendAsync(new { type = "complete", run = current }, true, ctx.Token).ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(700, ctx.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[EvalRoutes] progress stream error: " + e.Message);
                await sse.SendAsync(new { type = "error", message = "The eval progress stream failed." }, true, ctx.Token).ConfigureAwait(false);
            }
        }

        private async Task RunDetailAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string id = RouteHelper.Param(ctx, "id");
            EvalRun? run = await _Db.EvalRuns.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (run == null) { await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Run not found.").ConfigureAwait(false); return; }
            List<EvalResult> results = await _Db.EvalResults.EnumerateByRunAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { run, results }).ConfigureAwait(false);
        }

        private async Task DeleteRunAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId)) { await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false); return; }
            string id = RouteHelper.Param(ctx, "id");
            await _Db.EvalResults.DeleteByRunAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            await _Db.EvalRuns.DeleteAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }

        #endregion
    }
}
