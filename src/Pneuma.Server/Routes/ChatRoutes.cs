namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using Pneuma.Server.Streaming;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// The agentic chat assistant endpoint: a multi-turn, tool-using conversation streamed over
    /// server-sent events. Delegates the conversation loop to <see cref="AgenticChatService"/> and writes
    /// each emitted event (answer deltas, tool-call and tool-result events, and a final completion event
    /// carrying token telemetry) through an <see cref="SseWriter"/>.
    /// </summary>
    public class ChatRoutes
    {
        #region Private-Members

        private readonly AuthorizationService _Authz;
        private readonly AgenticChatService _Chat;
        private readonly ModelRunnerGate _Gate;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate chat routes.</summary>
        /// <param name="authz">Authorization service.</param>
        /// <param name="chat">Agentic chat service.</param>
        /// <param name="gate">Model-runner concurrency gate (admission control with HTTP 429 on saturation).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ChatRoutes(AuthorizationService authz, AgenticChatService chat, ModelRunnerGate gate, LoggingModule logging)
        {
            if (authz == null) throw new ArgumentNullException(nameof(authz));
            if (chat == null) throw new ArgumentNullException(nameof(chat));
            if (gate == null) throw new ArgumentNullException(nameof(gate));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Authz = authz;
            _Chat = chat;
            _Gate = gate;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/chat/stream", ChatStreamAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Multi-turn agentic chat over the corpus, streamed (SSE)", "Search"));
        }

        #endregion

        #region Private-Methods

        private async Task ChatStreamAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.GraphNode, OperationTypeEnum.Read, null, ctx.Token).ConfigureAwait(false))
            {
                await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
                return;
            }

            ChatRequest? request = RouteHelper.ReadBody<ChatRequest>(ctx);
            if (request == null || request.Messages == null || request.Messages.Count == 0)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "At least one message is required.").ConfigureAwait(false);
                return;
            }

            bool hasUserTurn = false;
            foreach (ChatTurn turn in request.Messages)
            {
                if (!String.IsNullOrWhiteSpace(turn.Content)) { hasUserTurn = true; break; }
            }
            if (!hasUserTurn)
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A non-empty message is required.").ConfigureAwait(false);
                return;
            }

            int max = Math.Clamp(request.MaxResults, 1, 20);

            // Admit through the model-runner gate before starting the SSE stream, so a saturated system
            // returns a clean HTTP 429 (rather than failing mid-stream). The slot is held for the whole
            // request; the assistant's internal tool calls run under this one slot without re-acquiring.
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
                SseWriter sse = new SseWriter(ctx);
                try
                {
                    await _Chat.RunAsync(rc, request.Messages, max, (payload, isFinal, token) => sse.SendAsync(payload, isFinal, token), ctx.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    _Logging.Warn("[ChatRoutes] chat stream error: " + e.Message);
                    await sse.SendAsync(new { type = "error", message = "The chat stream failed." }, true, ctx.Token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
