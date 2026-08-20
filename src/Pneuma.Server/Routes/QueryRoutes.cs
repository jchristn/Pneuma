namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Server.Streaming;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;
    using System.Threading.Tasks;

    /// <summary>
    /// Grounded question answering: delegates retrieval and answer synthesis to the shared
    /// <see cref="GroundedQueryService"/> (used identically by the MCP query tool), and exposes both a
    /// single-shot answer and a server-sent-events streaming answer.
    /// </summary>
    public class QueryRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly GroundedQueryService _Query;
        private readonly ModelRunnerGate _Gate;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate query routes.</summary>
        /// <param name="authz">Authorization service.</param>
        /// <param name="query">Shared grounded query service.</param>
        /// <param name="gate">Model-runner concurrency gate (admission control with HTTP 429 on saturation).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public QueryRoutes(AuthorizationService authz, GroundedQueryService query, ModelRunnerGate gate, LoggingModule logging)
        {
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Authz = authz;
            _Query = query;
            _Gate = gate;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/query", QueryAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Ask a grounded question of the corpus", "Search"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/query/stream", QueryStreamAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Ask a grounded question and stream the answer (SSE)", "Search"));
        }

        #endregion

        #region Private-Methods

        private async Task QueryAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            QueryRequest? request = RouteHelper.ReadBody<QueryRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Question))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A question is required.").ConfigureAwait(false);
                return;
            }

            int max = Math.Clamp(request.MaxResults, 1, 20);
            string tenantId = rc.TenantId ?? String.Empty;

            IDisposable lease;
            try
            {
                lease = await _Gate.AcquireAsync(ctx.Token).ConfigureAwait(false);
            }
            catch (ModelRunnerBusyException busy)
            {
                await RouteHelper.SendErrorAsync(ctx, 429, "TooManyRequests", busy.Message).ConfigureAwait(false);
                return;
            }

            using (lease)
            {
                GroundedAnswer answer = await _Query.AnswerAsync(tenantId, request.Question, max, request.SubjectId, ctx.Token).ConfigureAwait(false);
                QueryResponse response = new QueryResponse
                {
                    Answer = answer.Answer,
                    Sources = answer.Sources,
                    Grounded = answer.Grounded,
                    Model = answer.AnswerModel,
                    GenerationMs = answer.GenerationMs
                };
                await RouteHelper.SendJsonAsync(ctx, 200, response).ConfigureAwait(false);
            }
        }

        private async Task QueryStreamAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            QueryRequest? request = RouteHelper.ReadBody<QueryRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.Question))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A question is required.").ConfigureAwait(false);
                return;
            }

            int max = Math.Clamp(request.MaxResults, 1, 20);
            string tenantId = rc.TenantId ?? String.Empty;

            // Admit through the model-runner gate before starting the SSE stream so saturation returns a
            // clean HTTP 429 rather than failing mid-stream. The slot is held for the whole request.
            IDisposable lease;
            try
            {
                lease = await _Gate.AcquireAsync(ctx.Token).ConfigureAwait(false);
            }
            catch (ModelRunnerBusyException busy)
            {
                await RouteHelper.SendErrorAsync(ctx, 429, "TooManyRequests", busy.Message).ConfigureAwait(false);
                return;
            }

            SseWriter sse = new SseWriter(ctx);
            try
            {
                List<GraphNode> sources = await _Query.RetrieveSourcesAsync(tenantId, request.Question, max, request.SubjectId, ctx.Token).ConfigureAwait(false);
                await sse.SendAsync(new { type = "metadata", grounded = sources.Count > 0, sourceCount = sources.Count }, false, ctx.Token).ConfigureAwait(false);

                if (sources.Count == 0)
                {
                    await sse.SendAsync(new
                    {
                        type = "complete",
                        answer = "The archive does not contain enough information to answer that question.",
                        grounded = false,
                        insufficientSupport = true
                    }, true, ctx.Token).ConfigureAwait(false);
                    return;
                }

                ModelRunner? runner = await _Query.ResolveAnswerRunnerAsync(tenantId, ctx.Token).ConfigureAwait(false);
                if (runner == null)
                {
                    await sse.SendAsync(new
                    {
                        type = "complete",
                        answer = "No answering model is configured. The returned sources are relevant to your question.",
                        grounded = true,
                        insufficientSupport = false,
                        sources
                    }, true, ctx.Token).ConfigureAwait(false);
                    return;
                }

                GeneratedAnswer generated = await _Query.GenerateAnswerDetailedAsync(request.Question, sources, tenantId, runner, ctx.Token).ConfigureAwait(false);
                string answer = generated.Text;

                foreach (string chunk in SseWriter.SplitIntoChunks(answer, 48))
                {
                    ctx.Token.ThrowIfCancellationRequested();
                    await sse.SendAsync(new { type = "delta", text = chunk }, false, ctx.Token).ConfigureAwait(false);
                }

                await sse.SendAsync(new
                {
                    type = "complete",
                    answer,
                    grounded = true,
                    insufficientSupport = false,
                    model = generated.Model,
                    generationMs = generated.DurationMs,
                    sources
                }, true, ctx.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[QueryRoutes] streaming answer error: " + e.Message);
                await sse.SendAsync(new { type = "error", message = "The answer stream failed." }, true, ctx.Token).ConfigureAwait(false);
            }
            finally
            {
                lease.Dispose();
            }
        }

        #endregion
    }
}
