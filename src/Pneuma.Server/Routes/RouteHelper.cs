namespace Pneuma.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using WatsonWebserver.Core;

    /// <summary>
    /// Shared helpers for route handlers: context access, typed body reads, and JSON responses.
    /// </summary>
    public static class RouteHelper
    {
        /// <summary>Default exception handler for routes: log-free 500 with a JSON error body.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="e">Exception.</param>
        public static async Task ExceptionAsync(HttpContextBase ctx, Exception e)
        {
            // A malformed/undeserializable request body is a client error, not a server fault.
            if (e is RequestBodyException)
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.Send(Json.Serialize(new ErrorResponse("BadRequest", e.Message))).ConfigureAwait(false);
                return;
            }

            ctx.Response.StatusCode = 500;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(Json.Serialize(new ErrorResponse("InternalError", e.Message))).ConfigureAwait(false);
        }

        /// <summary>Read a required URL route parameter, returning empty string when absent.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="name">Parameter name.</param>
        /// <returns>The parameter value, or empty string.</returns>
        public static string Param(HttpContextBase ctx, string name)
        {
            return ctx.Request.Url.Parameters[name] ?? string.Empty;
        }

        /// <summary>
        /// Parse an <see cref="EnumerationQuery"/> from the request query string
        /// (maxResults, skip, order = asc|desc, search).
        /// </summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>The parsed enumeration query.</returns>
        public static EnumerationQuery ReadEnumerationQuery(HttpContextBase ctx)
        {
            System.Collections.Specialized.NameValueCollection? q = ctx.Request.Query.Elements;
            EnumerationQuery query = new EnumerationQuery();

            string? maxResults = q?["maxResults"];
            if (!String.IsNullOrEmpty(maxResults) && Int32.TryParse(maxResults, out int mr)) query.MaxResults = mr;

            string? skip = q?["skip"];
            if (!String.IsNullOrEmpty(skip) && Int32.TryParse(skip, out int sk)) query.Skip = sk;

            string? order = q?["order"];
            if (!String.IsNullOrEmpty(order))
            {
                query.Ordering = order.StartsWith("asc", StringComparison.OrdinalIgnoreCase)
                    ? EnumerationOrderEnum.CreatedAscending
                    : EnumerationOrderEnum.CreatedDescending;
            }

            string? search = q?["search"];
            if (!String.IsNullOrWhiteSpace(search)) query.Search = search;

            return query;
        }

        /// <summary>Read the typed request context from the HTTP context metadata.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Request context, or a new empty one if absent.</returns>
        public static RequestContext Context(HttpContextBase ctx)
        {
            return ctx.Metadata as RequestContext ?? new RequestContext();
        }

        /// <summary>Deserialize the request body into a typed DTO.</summary>
        /// <typeparam name="T">DTO type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <returns>Deserialized DTO, or default when the body is empty or invalid.</returns>
        public static T? ReadBody<T>(HttpContextBase ctx)
        {
            string? body = ctx.Request.DataAsString;
            // An absent body is a distinct case from a malformed one: callers treat null as "no body".
            if (String.IsNullOrWhiteSpace(body)) return default;
            try
            {
                return Json.Deserialize<T>(body);
            }
            catch (Exception e)
            {
                // A present-but-unparseable body (unknown enum value, type mismatch, invalid JSON) must
                // not be swallowed into a null — that yields misleading "X is required" errors. Surface
                // the real reason as a 400 via the route exception handler.
                throw new RequestBodyException("Invalid request body: " + DescribeBodyError(e));
            }
        }

        private static string DescribeBodyError(Exception e)
        {
            System.Text.Json.JsonException? jsonError = e as System.Text.Json.JsonException ?? e.InnerException as System.Text.Json.JsonException;
            string message = (jsonError ?? e).Message;
            // Trim the trailing JSON path/position detail that System.Text.Json appends, keeping the reason.
            int pathMarker = message.IndexOf(" Path:", StringComparison.Ordinal);
            if (pathMarker > 0) message = message.Substring(0, pathMarker);
            return message.Trim();
        }

        /// <summary>Send a JSON response with a status code.</summary>
        /// <typeparam name="T">Payload type.</typeparam>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="status">HTTP status code.</param>
        /// <param name="payload">Payload.</param>
        public static async Task SendJsonAsync<T>(HttpContextBase ctx, int status, T payload)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(Json.Serialize(payload)).ConfigureAwait(false);
        }

        /// <summary>Send a JSON error response.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="status">HTTP status code.</param>
        /// <param name="code">Error code.</param>
        /// <param name="message">Error message.</param>
        public static async Task SendErrorAsync(HttpContextBase ctx, int status, string code, string message)
        {
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(Json.Serialize(new ErrorResponse(code, message))).ConfigureAwait(false);
        }
    }
}
