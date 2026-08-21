namespace Pneuma.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Server.Services;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Chat history and feedback routes: list/inspect persisted chat turns, list feedback, and submit a
    /// thumbs up/down with an optional comment against a turn.
    /// </summary>
    public class ChatHistoryRoutes
    {
        #region Private-Members

        private readonly DatabaseDriverBase _Db;
        private readonly AuthorizationService _Authz;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate chat history routes.</summary>
        /// <param name="db">Database driver.</param>
        /// <param name="authz">Authorization service.</param>
        /// <exception cref="ArgumentNullException">Thrown when a dependency is null.</exception>
        public ChatHistoryRoutes(DatabaseDriverBase db, AuthorizationService authz)
        {
            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Authz = authz ?? throw new ArgumentNullException(nameof(authz));
        }

        #endregion

        #region Public-Methods

        /// <summary>Register routes.</summary>
        /// <param name="server">Watson server.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="server"/> is null.</exception>
        public void Register(Webserver server)
        {
            if (server == null) throw new ArgumentNullException(nameof(server));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/history", ListHistoryAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List persisted chat turns", "History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/history/{id}", HistoryDetailAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get a chat turn with its feedback", "History"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/feedback", ListFeedbackAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List chat feedback", "Feedback"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/feedback", SubmitFeedbackAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Submit feedback on a chat answer", "Feedback"));

            server.Routes.PostAuthentication.Static.Add(HttpMethod.GET, "/v1.0/threads", ListThreadsAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("List conversation threads", "History"));
            server.Routes.PostAuthentication.Static.Add(HttpMethod.POST, "/v1.0/threads", CreateThreadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Create a conversation thread", "History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.GET, "/v1.0/threads/{id}", ThreadDetailAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Get a thread with its turns", "History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.PUT, "/v1.0/threads/{id}", RenameThreadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Rename a conversation thread", "History"));
            server.Routes.PostAuthentication.Parameter.Add(HttpMethod.DELETE, "/v1.0/threads/{id}", DeleteThreadAsync, RouteHelper.ExceptionAsync,
                openApiMetadata: OpenApiRouteMetadata.Create("Delete a conversation thread and its turns", "History"));
        }

        #endregion

        #region Private-Methods

        private async Task<bool> GateAsync(HttpContextBase ctx, RequestContext rc, OperationTypeEnum op)
        {
            if (await _Authz.AuthorizeAsync(rc, ResourceTypeEnum.Subject, op, null, ctx.Token).ConfigureAwait(false)) return true;
            await RouteHelper.SendErrorAsync(ctx, 403, "Forbidden", "Not permitted.").ConfigureAwait(false);
            return false;
        }

        private async Task ListHistoryAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            string? threadId = ctx.Request.Query.Elements?["threadId"];
            List<ChatTurnRecord> turns = String.IsNullOrEmpty(threadId)
                ? await _Db.ChatTurns.EnumerateAsync(rc.TenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, ctx.Token).ConfigureAwait(false)
                : await _Db.ChatTurns.EnumerateByThreadAsync(rc.TenantId, threadId!, ctx.Token).ConfigureAwait(false);
            EnumerationResult<ChatTurnRecord> result = EnumerationHelper.Paginate(turns, RouteHelper.ReadEnumerationQuery(ctx), t => t.CreatedUtc, t => t.Id);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task HistoryDetailAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            ChatTurnRecord? turn = await _Db.ChatTurns.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (turn == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Chat turn not found.").ConfigureAwait(false);
                return;
            }
            List<ChatFeedback> feedback = new List<ChatFeedback>();
            List<ChatFeedback> all = await _Db.ChatFeedback.EnumerateAsync(rc.TenantId, turn.SubjectId, ctx.Token).ConfigureAwait(false);
            foreach (ChatFeedback item in all)
            {
                if (String.Equals(item.TurnId, turn.Id, StringComparison.Ordinal)) feedback.Add(item);
            }
            List<ChatToolCall> toolCalls = await _Db.ChatToolCalls.EnumerateByTurnAsync(rc.TenantId, turn.Id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new ChatTurnDetail { Turn = turn, Feedback = feedback, ToolCalls = toolCalls }).ConfigureAwait(false);
        }

        private async Task ListThreadsAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            List<ChatThread> threads = await _Db.ChatThreads.EnumerateAsync(rc.TenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, ctx.Token).ConfigureAwait(false);
            EnumerationResult<ChatThread> result = EnumerationHelper.Paginate(threads, RouteHelper.ReadEnumerationQuery(ctx), t => t.LastActivityUtc, t => t.Id);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task CreateThreadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Create).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            ThreadRequest? request = RouteHelper.ReadBody<ThreadRequest>(ctx);
            ChatThread created = await _Db.ChatThreads.CreateAsync(new ChatThread
            {
                TenantId = rc.TenantId,
                SubjectId = String.IsNullOrWhiteSpace(request?.SubjectId) ? null : request!.SubjectId,
                UserId = rc.UserId,
                Title = String.IsNullOrWhiteSpace(request?.Title) ? "New conversation" : request!.Title!.Trim()
            }, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        private async Task ThreadDetailAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            ChatThread? thread = await _Db.ChatThreads.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (thread == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Thread not found.").ConfigureAwait(false);
                return;
            }
            List<ChatTurnRecord> turns = await _Db.ChatTurns.EnumerateByThreadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, new { thread, turns }).ConfigureAwait(false);
        }

        private async Task RenameThreadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Update).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            ChatThread? thread = await _Db.ChatThreads.ReadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            if (thread == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "Thread not found.").ConfigureAwait(false);
                return;
            }
            ThreadRequest? request = RouteHelper.ReadBody<ThreadRequest>(ctx);
            if (!String.IsNullOrWhiteSpace(request?.Title)) thread.Title = request!.Title!.Trim();
            ChatThread updated = await _Db.ChatThreads.UpdateAsync(thread, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 200, updated).ConfigureAwait(false);
        }

        private async Task DeleteThreadAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Delete).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string id = RouteHelper.Param(ctx, "id");
            // Cascade: remove each turn's tool calls, then the turns, then the thread. Best-effort ordering.
            List<ChatTurnRecord> turns = await _Db.ChatTurns.EnumerateByThreadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            foreach (ChatTurnRecord turn in turns)
            {
                await _Db.ChatToolCalls.DeleteByTurnAsync(rc.TenantId, turn.Id, ctx.Token).ConfigureAwait(false);
            }
            await _Db.ChatTurns.DeleteByThreadAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            await _Db.ChatThreads.DeleteAsync(rc.TenantId, id, ctx.Token).ConfigureAwait(false);
            ctx.Response.StatusCode = 204;
            await ctx.Response.Send(ctx.Token).ConfigureAwait(false);
        }

        private async Task ListFeedbackAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            if (!await GateAsync(ctx, rc, OperationTypeEnum.Read).ConfigureAwait(false)) return;
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            string? subjectId = ctx.Request.Query.Elements?["subjectId"];
            List<ChatFeedback> feedback = await _Db.ChatFeedback.EnumerateAsync(rc.TenantId, String.IsNullOrEmpty(subjectId) ? null : subjectId, ctx.Token).ConfigureAwait(false);

            // Enrich each feedback with its rated turn so the Feedback modal can show the full prompt/response
            // without a second round trip.
            List<ChatFeedbackDetail> enriched = new List<ChatFeedbackDetail>();
            Dictionary<string, ChatTurnRecord?> turnCache = new Dictionary<string, ChatTurnRecord?>(StringComparer.Ordinal);
            foreach (ChatFeedback item in feedback)
            {
                if (!turnCache.TryGetValue(item.TurnId, out ChatTurnRecord? turn))
                {
                    turn = await _Db.ChatTurns.ReadAsync(rc.TenantId, item.TurnId, ctx.Token).ConfigureAwait(false);
                    turnCache[item.TurnId] = turn;
                }
                enriched.Add(new ChatFeedbackDetail { Feedback = item, Turn = turn });
            }

            EnumerationResult<ChatFeedbackDetail> result = EnumerationHelper.Paginate(enriched, RouteHelper.ReadEnumerationQuery(ctx), f => f.Feedback.CreatedUtc, f => f.Feedback.Id);
            await RouteHelper.SendJsonAsync(ctx, 200, result).ConfigureAwait(false);
        }

        private async Task SubmitFeedbackAsync(HttpContextBase ctx)
        {
            RequestContext rc = RouteHelper.Context(ctx);
            // Any authenticated user may rate an answer; no additional RBAC gate beyond authentication.
            if (String.IsNullOrEmpty(rc.TenantId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Tenant could not be resolved.").ConfigureAwait(false);
                return;
            }
            SubmitFeedbackRequest? request = RouteHelper.ReadBody<SubmitFeedbackRequest>(ctx);
            if (request == null || String.IsNullOrWhiteSpace(request.TurnId))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "A turnId is required.").ConfigureAwait(false);
                return;
            }
            if (request.Rating == FeedbackRatingEnum.None && String.IsNullOrWhiteSpace(request.Comment))
            {
                await RouteHelper.SendErrorAsync(ctx, 400, "BadRequest", "Provide a rating and/or a comment.").ConfigureAwait(false);
                return;
            }

            ChatTurnRecord? turn = await _Db.ChatTurns.ReadAsync(rc.TenantId, request.TurnId!, ctx.Token).ConfigureAwait(false);
            if (turn == null)
            {
                await RouteHelper.SendErrorAsync(ctx, 404, "NotFound", "The chat turn being rated was not found.").ConfigureAwait(false);
                return;
            }

            ChatFeedback feedback = new ChatFeedback
            {
                TenantId = rc.TenantId,
                TurnId = turn.Id,
                SubjectId = turn.SubjectId,
                UserId = rc.UserId,
                Rating = request.Rating,
                Comment = String.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment
            };
            ChatFeedback created = await _Db.ChatFeedback.CreateAsync(feedback, ctx.Token).ConfigureAwait(false);
            await RouteHelper.SendJsonAsync(ctx, 201, created).ConfigureAwait(false);
        }

        #endregion
    }
}
