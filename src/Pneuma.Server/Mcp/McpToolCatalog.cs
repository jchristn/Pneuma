namespace Pneuma.Server.Mcp
{
    using System.Collections.Generic;
    using PolyPrompt.Models;

    /// <summary>
    /// Static catalog of MCP protocol metadata for the Pneuma endpoint: the <c>initialize</c> result, the
    /// capabilities description, and the <c>tools/list</c> schema. Kept separate from request handling so
    /// the tool contract is defined in one place and the dispatcher stays small.
    /// </summary>
    public static class McpToolCatalog
    {
        #region Public-Methods

        /// <summary>Build the <c>initialize</c> result (protocol version, server info, capabilities).</summary>
        /// <returns>The initialize result payload.</returns>
        public static object BuildInitializeResult()
        {
            return new
            {
                protocolVersion = "2024-11-05",
                serverInfo = new { name = "Pneuma", version = "1.0.0" },
                capabilities = new { tools = new { } }
            };
        }

        /// <summary>Build the <c>pneuma_capabilities</c> tool payload describing the platform and paging protocol.</summary>
        /// <returns>The capabilities payload.</returns>
        public static object BuildCapabilities()
        {
            return new
            {
                platform = "Pneuma",
                description = "Generalized knowledge-graph hydration platform.",
                tools = new[] { "pneuma_capabilities", "pneuma_enumerate_subjects", "pneuma_get_subject", "pneuma_enumerate_jobs", "pneuma_get_job", "pneuma_enumerate_links", "pneuma_get_link", "pneuma_search", "pneuma_get_node", "pneuma_get_neighbors", "pneuma_query" },
                enumeration = "Collections are paged. Call an pneuma_enumerate_* tool with skip=0; the first result's totalRecords is the exact count. Advance skip by the page size and repeat until endOfResults is true (equivalently recordsRemaining reaches 0). Enumeration objects are small summaries — fetch a full object individually with the matching pneuma_get_* tool."
            };
        }

        /// <summary>Build the <c>tools/list</c> tool descriptors with their JSON-Schema input contracts.</summary>
        /// <returns>The list of tool descriptors.</returns>
        public static List<object> BuildToolList()
        {
            return new List<object>
            {
                new
                {
                    name = "pneuma_capabilities",
                    description = "Describe the Pneuma platform and how to enumerate objects. Returns the tool list and the paging protocol.",
                    inputSchema = new { type = "object", properties = new { } }
                },
                new
                {
                    name = "pneuma_enumerate_subjects",
                    description = "Enumerate subjects as small summaries, paged. Start with skip=0; the first result's totalRecords is the exact total. Advance skip by maxResults and call again until endOfResults is true. Use pneuma_get_subject to fetch a full subject by id. Example: {\"maxResults\":50,\"skip\":0} then {\"maxResults\":50,\"skip\":50}.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            maxResults = new { type = "integer", description = "Page size (clamped by the server)." },
                            skip = new { type = "integer", description = "Number of records to skip." },
                            order = new { type = "string", description = "asc or desc by creation time." },
                            search = new { type = "string", description = "Optional case-insensitive name filter." }
                        }
                    }
                },
                new
                {
                    name = "pneuma_get_subject",
                    description = "Fetch a single full subject by id. Use this to retrieve the complete object after locating it via pneuma_enumerate_subjects.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { id = new { type = "string", description = "Subject id." } },
                        required = new[] { "id" }
                    }
                },
                new
                {
                    name = "pneuma_enumerate_jobs",
                    description = "Enumerate ingestion jobs as small summaries, paged. Same paging protocol as pneuma_enumerate_subjects: start skip=0, read totalRecords, advance skip until endOfResults. Use pneuma_get_job for the full record. An optional \"status\" argument filters by job status.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            maxResults = new { type = "integer", description = "Page size (clamped by the server)." },
                            skip = new { type = "integer", description = "Number of records to skip." },
                            order = new { type = "string", description = "asc or desc by creation time." }
                        }
                    }
                },
                new
                {
                    name = "pneuma_get_job",
                    description = "Fetch a single full ingestion job by id, including its stage, status, error, and graph/index ids.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { id = new { type = "string", description = "Ingestion job id." } },
                        required = new[] { "id" }
                    }
                },
                new
                {
                    name = "pneuma_enumerate_links",
                    description = "Enumerate content links (ingestion sources) as small summaries, paged. Same paging protocol as the other enumerate tools. Use pneuma_get_link for the full record.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            maxResults = new { type = "integer", description = "Page size (clamped by the server)." },
                            skip = new { type = "integer", description = "Number of records to skip." },
                            order = new { type = "string", description = "asc or desc by creation time." }
                        }
                    }
                },
                new
                {
                    name = "pneuma_get_link",
                    description = "Fetch a single full content link by id, including its url, title, status, and last error.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { id = new { type = "string", description = "Content link id." } },
                        required = new[] { "id" }
                    }
                },
                new
                {
                    name = "pneuma_search",
                    description = "Full-text search the ingested corpus, returning a bounded, ranked set of graph-node summaries (id, name, type, score). This is a top-N query, not a full enumeration; raise 'max' to widen it.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            query = new { type = "string", description = "Search query text." },
                            max = new { type = "integer", description = "Maximum hits (clamped 1..100)." }
                        },
                        required = new[] { "query" }
                    }
                },
                new
                {
                    name = "pneuma_get_node",
                    description = "Fetch a single knowledge-graph node by id, including its type, content, labels, and tags.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { id = new { type = "string", description = "Graph node id." } },
                        required = new[] { "id" }
                    }
                },
                new
                {
                    name = "pneuma_get_neighbors",
                    description = "Fetch the adjacent nodes of a graph node by id, as a bounded set of small summaries (id, name, type). Use pneuma_get_node for a neighbor's full record.",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new { id = new { type = "string", description = "Graph node id." } },
                        required = new[] { "id" }
                    }
                },
                new
                {
                    name = "pneuma_query",
                    description = "Ask a grounded natural-language question of the corpus. Returns a cited answer, the supporting source-node summaries, whether the answer is grounded, and whether the corpus lacked enough support (insufficientSupport). Pass stream:true to receive the answer as a Server-Sent Events stream (delta events, the final event being the JSON-RPC result).",
                    inputSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            question = new { type = "string", description = "The natural-language question." },
                            max = new { type = "integer", description = "Maximum supporting sources (clamped 1..20)." },
                            stream = new { type = "boolean", description = "When true, respond as an SSE stream of delta events ending with the JSON-RPC result." }
                        },
                        required = new[] { "question" }
                    }
                }
            };
        }

        /// <summary>
        /// Build the read tools as PolyPrompt <see cref="ToolDefinition"/> objects for native function
        /// calling by the agentic chat assistant. These mirror <see cref="BuildToolList"/> but in the
        /// provider-neutral tool-definition shape. The grounded-answer tool (<c>pneuma_query</c>) is
        /// intentionally excluded: the assistant is itself the answering layer and should gather context
        /// with search/get tools rather than invoke a nested model call.
        /// </summary>
        /// <returns>The assistant's callable tool definitions.</returns>
        public static List<ToolDefinition> BuildAssistantToolDefinitions()
        {
            List<ToolDefinition> tools = new List<ToolDefinition>();

            tools.Add(ToolDefinition.Function("pneuma_search",
                "Full-text search the ingested corpus. Returns a bounded, ranked set of graph-node summaries (id, name, type, score). Use this first to locate relevant nodes, then pneuma_get_node for full content.",
                ObjectSchema(
                    new Dictionary<string, object>
                    {
                        ["query"] = StringProp("Search query text."),
                        ["max"] = IntProp("Maximum hits (clamped 1..100).")
                    },
                    new[] { "query" })));

            tools.Add(ToolDefinition.Function("pneuma_get_node",
                "Fetch a single knowledge-graph node by id, including its type, content, labels, and tags.",
                ObjectSchema(new Dictionary<string, object> { ["id"] = StringProp("Graph node id.") }, new[] { "id" })));

            tools.Add(ToolDefinition.Function("pneuma_get_neighbors",
                "Fetch the adjacent nodes of a graph node by id, as a bounded set of summaries (id, name, type). Use pneuma_get_node for a neighbor's full record.",
                ObjectSchema(new Dictionary<string, object> { ["id"] = StringProp("Graph node id.") }, new[] { "id" })));

            tools.Add(ToolDefinition.Function("pneuma_enumerate_subjects",
                "Enumerate subjects as small summaries, paged. Start with skip=0; the first result's totalRecords is the exact total. Advance skip by maxResults until endOfResults is true. Use pneuma_get_subject for a full subject.",
                ObjectSchema(PagingProperties("Optional case-insensitive name filter."), null)));

            tools.Add(ToolDefinition.Function("pneuma_get_subject",
                "Fetch a single full subject by id.",
                ObjectSchema(new Dictionary<string, object> { ["id"] = StringProp("Subject id.") }, new[] { "id" })));

            tools.Add(ToolDefinition.Function("pneuma_enumerate_jobs",
                "Enumerate ingestion jobs as small summaries, paged. Same paging protocol as pneuma_enumerate_subjects. Use pneuma_get_job for the full record.",
                ObjectSchema(PagingProperties(null), null)));

            tools.Add(ToolDefinition.Function("pneuma_get_job",
                "Fetch a single full ingestion job by id, including its stage, status, and error.",
                ObjectSchema(new Dictionary<string, object> { ["id"] = StringProp("Ingestion job id.") }, new[] { "id" })));

            tools.Add(ToolDefinition.Function("pneuma_enumerate_links",
                "Enumerate content links (ingestion sources) as small summaries, paged. Same paging protocol. Use pneuma_get_link for the full record.",
                ObjectSchema(PagingProperties(null), null)));

            tools.Add(ToolDefinition.Function("pneuma_get_link",
                "Fetch a single full content link by id, including its url, title, status, and last error.",
                ObjectSchema(new Dictionary<string, object> { ["id"] = StringProp("Content link id.") }, new[] { "id" })));

            tools.Add(ToolDefinition.Function("pneuma_capabilities",
                "Describe the Pneuma platform and how to enumerate objects. Returns the tool list and the paging protocol.",
                ObjectSchema(new Dictionary<string, object>(), null)));

            return tools;
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, object> ObjectSchema(Dictionary<string, object> properties, string[]? required)
        {
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = properties
            };
            if (required != null && required.Length > 0) schema["required"] = required;
            return schema;
        }

        private static Dictionary<string, object> StringProp(string description)
        {
            return new Dictionary<string, object> { ["type"] = "string", ["description"] = description };
        }

        private static Dictionary<string, object> IntProp(string description)
        {
            return new Dictionary<string, object> { ["type"] = "integer", ["description"] = description };
        }

        private static Dictionary<string, object> PagingProperties(string? searchDescription)
        {
            Dictionary<string, object> properties = new Dictionary<string, object>
            {
                ["maxResults"] = IntProp("Page size (clamped by the server)."),
                ["skip"] = IntProp("Number of records to skip."),
                ["order"] = StringProp("asc or desc by creation time.")
            };
            if (searchDescription != null) properties["search"] = StringProp(searchDescription);
            return properties;
        }

        #endregion
    }
}
