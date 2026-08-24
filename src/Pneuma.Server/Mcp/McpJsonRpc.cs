namespace Pneuma.Server.Mcp
{
    using System;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Routes;
    using WatsonWebserver.Core;

    /// <summary>
    /// JSON-RPC helpers shared by the MCP endpoint: request-id extraction, typed argument reading,
    /// <see cref="EnumerationQuery"/> parsing from tool arguments, and success/error response writers.
    /// Every MCP response is sent as HTTP 200 with a JSON-RPC envelope, matching the protocol.
    /// </summary>
    public static class McpJsonRpc
    {
        #region Public-Methods

        /// <summary>Extract the JSON-RPC request id (string, number, or null) from a request root.</summary>
        /// <param name="root">The request root element.</param>
        /// <returns>The id as a string/long/double, or null.</returns>
        public static object? ExtractId(JsonElement root)
        {
            if (!root.TryGetProperty("id", out JsonElement idElement)) return null;
            switch (idElement.ValueKind)
            {
                case JsonValueKind.String:
                    return idElement.GetString();
                case JsonValueKind.Number:
                    return idElement.TryGetInt64(out long number) ? number : (object?)idElement.GetDouble();
                default:
                    return null;
            }
        }

        /// <summary>Read a required string argument from a tool arguments object.</summary>
        /// <param name="arguments">The arguments element.</param>
        /// <param name="name">The property name.</param>
        /// <returns>The string value, or empty when absent.</returns>
        public static string GetStringArgument(JsonElement arguments, string name)
        {
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? String.Empty;
            }
            return String.Empty;
        }

        /// <summary>True when the tool arguments request a streaming (SSE) response.</summary>
        /// <param name="arguments">The arguments element.</param>
        /// <returns>True when <c>stream</c> is present and true.</returns>
        /// <summary>
        /// Parse an optional <c>metadataFilter</c> object from tool arguments into a typed
        /// <see cref="RetrievalFilter"/>. Returns null when the argument is absent, not an object, or empty of
        /// predicates, so callers can pass it straight through to the retrieval path.
        /// </summary>
        /// <param name="arguments">The tool arguments element.</param>
        /// <returns>The parsed filter, or null when none was supplied.</returns>
        public static RetrievalFilter? FilterFromArguments(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object) return null;
            if (!arguments.TryGetProperty("metadataFilter", out JsonElement filterElement)) return null;
            if (filterElement.ValueKind != JsonValueKind.Object) return null;

            RetrievalFilter? filter = Json.Deserialize<RetrievalFilter>(filterElement.GetRawText());
            if (filter == null || filter.IsEmpty()) return null;
            return filter;
        }

        public static bool IsStreamRequested(JsonElement arguments)
        {
            return arguments.ValueKind == JsonValueKind.Object
                && arguments.TryGetProperty("stream", out JsonElement stream)
                && stream.ValueKind == JsonValueKind.True;
        }

        /// <summary>Parse an <see cref="EnumerationQuery"/> (maxResults, skip, order, search) from tool arguments.</summary>
        /// <param name="arguments">The arguments element.</param>
        /// <returns>The parsed query with defaults for any absent fields.</returns>
        public static EnumerationQuery QueryFromArguments(JsonElement arguments)
        {
            EnumerationQuery query = new EnumerationQuery();
            if (arguments.ValueKind != JsonValueKind.Object) return query;

            if (arguments.TryGetProperty("maxResults", out JsonElement mr) && mr.ValueKind == JsonValueKind.Number && mr.TryGetInt32(out int maxResults)) query.MaxResults = maxResults;
            if (arguments.TryGetProperty("skip", out JsonElement sk) && sk.ValueKind == JsonValueKind.Number && sk.TryGetInt32(out int skip)) query.Skip = skip;
            if (arguments.TryGetProperty("order", out JsonElement or) && or.ValueKind == JsonValueKind.String)
            {
                string order = or.GetString() ?? String.Empty;
                query.Ordering = order.StartsWith("asc", StringComparison.OrdinalIgnoreCase)
                    ? EnumerationOrderEnum.CreatedAscending
                    : EnumerationOrderEnum.CreatedDescending;
            }
            if (arguments.TryGetProperty("search", out JsonElement se) && se.ValueKind == JsonValueKind.String)
            {
                string? search = se.GetString();
                if (!String.IsNullOrWhiteSpace(search)) query.Search = search;
            }
            return query;
        }

        /// <summary>Build a paged enumeration payload (the <see cref="EnumerationResult{T}"/> envelope shape).</summary>
        /// <param name="maxResults">The page size applied.</param>
        /// <param name="skip">The number of records skipped.</param>
        /// <param name="totalRecords">The exact total across all pages.</param>
        /// <param name="recordsRemaining">Records remaining after this page.</param>
        /// <param name="endOfResults">Whether this is the last page.</param>
        /// <param name="objects">The page's objects (small summaries).</param>
        /// <returns>The enumeration payload.</returns>
        public static object BuildPage(int maxResults, int skip, long totalRecords, long recordsRemaining, bool endOfResults, System.Collections.Generic.List<object> objects)
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

        /// <summary>Send a JSON-RPC success result.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="id">The request id to echo.</param>
        /// <param name="result">The result payload.</param>
        /// <returns>A task.</returns>
        public static async Task SendResultAsync(HttpContextBase ctx, object? id, object? result)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, new McpSuccessResponse(id, result)).ConfigureAwait(false);
        }

        /// <summary>Send a JSON-RPC error.</summary>
        /// <param name="ctx">HTTP context.</param>
        /// <param name="id">The request id to echo.</param>
        /// <param name="code">The JSON-RPC error code.</param>
        /// <param name="message">The error message.</param>
        /// <returns>A task.</returns>
        public static async Task SendErrorAsync(HttpContextBase ctx, object? id, int code, string message)
        {
            await RouteHelper.SendJsonAsync(ctx, 200, new McpErrorResponse(id, code, message)).ConfigureAwait(false);
        }

        #endregion
    }
}
