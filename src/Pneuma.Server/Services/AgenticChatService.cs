namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Database;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Security;
    using Pneuma.Core.Serialization;
    using Pneuma.Server.Mcp;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// The agentic chat assistant: drives a multi-turn conversation against the tenant's answering model,
    /// letting the model call Pneuma's read tools (search, node/neighbor fetches, entity enumeration) via
    /// native function calling until it produces a final answer. Output is streamed to the caller through an
    /// emit delegate (answer deltas, tool-call and tool-result events, and a final completion event carrying
    /// token telemetry). Tool execution is delegated to <see cref="PneumaToolExecutor"/>, so the assistant
    /// and the MCP endpoint expose exactly the same tools.
    /// </summary>
    public class AgenticChatService
    {
        #region Private-Members

        private readonly int _MaxIterations;

        private const string _FallbackSystemPrompt =
            "You are Pneuma's knowledge assistant. Answer questions about the curated knowledge graph using the " +
            "provided tools to retrieve grounded facts. Prefer pneuma_search to find relevant nodes, then " +
            "pneuma_get_node for detail. Cite what you find and say plainly when the corpus does not support an answer. " +
            "Format answers in Markdown.";

        private readonly DatabaseDriverBase _Db;
        private readonly GroundedQueryService _Query;
        private readonly PneumaToolExecutor _Tools;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the agentic chat service.</summary>
        /// <param name="db">Database driver (system-prompt lookup).</param>
        /// <param name="query">Shared grounded query service (runner resolution).</param>
        /// <param name="tools">Context-free tool executor.</param>
        /// <param name="cipher">Cipher for decrypting model-runner keys.</param>
        /// <param name="maxToolIterations">Maximum tool-calling iterations before a final answer is forced (>= 1).</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public AgenticChatService(DatabaseDriverBase db, GroundedQueryService query, PneumaToolExecutor tools, Aes256Cipher cipher, int maxToolIterations, LoggingModule logging)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (tools == null) throw new ArgumentNullException(nameof(tools));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Db = db;
            _Query = query;
            _Tools = tools;
            _Cipher = cipher;
            _MaxIterations = Math.Max(1, maxToolIterations);
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run the assistant over a conversation, streaming events through <paramref name="emit"/>. The emit
        /// delegate receives a JSON-serializable payload and a flag marking the final event.
        /// </summary>
        /// <param name="rc">Request context of the calling principal (used to authorize tool calls).</param>
        /// <param name="turns">The conversation so far, oldest first.</param>
        /// <param name="maxResults">Retrieval bound passed through to tool calls.</param>
        /// <param name="subjectId">Optional subject to scope the assistant's retrieval tools to; null searches the whole tenant.</param>
        /// <param name="emit">Async delegate that writes one event: (payload, isFinal, token).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A task.</returns>
        public async Task RunAsync(RequestContext rc, List<ChatTurn> turns, int maxResults, string? subjectId, Func<object, bool, CancellationToken, Task> emit, CancellationToken token)
        {
            if (rc == null) throw new ArgumentNullException(nameof(rc));
            if (turns == null) throw new ArgumentNullException(nameof(turns));
            if (emit == null) throw new ArgumentNullException(nameof(emit));

            string tenantId = rc.TenantId ?? String.Empty;

            ModelRunner? runner = await _Query.ResolveAnswerRunnerAsync(tenantId, token).ConfigureAwait(false);
            if (runner == null)
            {
                await emit(new { type = "complete", answer = "No answering model is configured. Ask an administrator to add a model runner.", model = (string?)null }, true, token).ConfigureAwait(false);
                return;
            }

            Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(tenantId, "assistant.system", token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(prompt?.Content) ? _FallbackSystemPrompt : prompt!.Content;

            string? apiKey = DecryptKey(runner);
            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
            List<ToolDefinition> tools = McpToolCatalog.BuildAssistantToolDefinitions();

            List<ChatMessage> messages = new List<ChatMessage>();
            messages.Add(ChatMessage.System(systemPrompt));
            foreach (ChatTurn turn in turns)
            {
                string content = turn.Content ?? String.Empty;
                if (String.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)) messages.Add(ChatMessage.Assistant(content));
                else messages.Add(ChatMessage.User(content));
            }

            long promptTokens = 0;
            long completionTokens = 0;
            long totalTokens = 0;
            long timeToFirstTokenMs = -1;
            long generationMs = 0;
            string model = runner.DefaultModel ?? String.Empty;
            List<object> toolTrace = new List<object>();
            // The content links the assistant's search/answer tools drew from (best relevance score each),
            // resolved to citations at the end.
            Dictionary<string, double> citedLinkScores = new Dictionary<string, double>(StringComparer.Ordinal);
            string finalAnswer = String.Empty;
            bool producedText = false;

            try
            {
                for (int iteration = 0; iteration < _MaxIterations; iteration++)
                {
                    token.ThrowIfCancellationRequested();

                    ToolChatRequest request = new ToolChatRequest
                    {
                        Messages = messages,
                        Tools = tools,
                        ToolChoice = "auto",
                        Temperature = 0.2,
                        MaxTokens = 1024
                    };

                    // On the final permitted iteration, force a text answer so the loop always terminates.
                    // Disabling tools alone is not enough — small local models can return an empty completion
                    // when simply told "no tools", so add an explicit instruction to synthesize an answer now
                    // from what has already been gathered.
                    if (iteration == _MaxIterations - 1)
                    {
                        request.Tools = new List<ToolDefinition>();
                        request.ToolChoice = "none";
                        messages.Add(ChatMessage.User(
                            "Using the information already gathered from the tools above, write your final answer to my question now. "
                            + "Do not call any tools. If the archive does not contain enough information to answer fully, say so briefly "
                            + "and summarize whatever relevant material was found."));
                    }

                    ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, token).ConfigureAwait(false);
                    if (response == null || !response.Success)
                    {
                        _Logging.Warn("[AgenticChatService] chat request failed: " + (response?.Error ?? "no response"));
                        if (!producedText)
                        {
                            await emit(new { type = "error", message = "The assistant could not generate a response." }, true, token).ConfigureAwait(false);
                            return;
                        }
                        break;
                    }

                    // Drain the stream fully; only then are the aggregate fields (ToolCalls, Usage, timing) populated.
                    await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(token).ConfigureAwait(false))
                    {
                        if (!String.IsNullOrEmpty(chunk.Text))
                        {
                            producedText = true;
                            await emit(new { type = "delta", text = chunk.Text }, false, token).ConfigureAwait(false);
                        }
                    }

                    if (timeToFirstTokenMs < 0 && response.TimeToFirstTokenMs > 0) timeToFirstTokenMs = response.TimeToFirstTokenMs;
                    generationMs += response.OverallRuntimeMs;
                    if (response.Usage != null)
                    {
                        promptTokens += response.Usage.PromptTokens ?? 0;
                        completionTokens += response.Usage.CompletionTokens ?? 0;
                        totalTokens += response.Usage.TotalTokens ?? 0;
                    }
                    if (!String.IsNullOrEmpty(response.Model)) model = response.Model;

                    List<ToolCall> calls = response.ToolCalls ?? new List<ToolCall>();
                    if (calls.Count == 0)
                    {
                        finalAnswer = response.Text ?? String.Empty;
                        break;
                    }

                    // Record the assistant's tool-call turn, then execute each tool and feed the result back.
                    messages.Add(response.ToAssistantMessage());
                    foreach (ToolCall call in calls)
                    {
                        token.ThrowIfCancellationRequested();
                        await emit(new { type = "tool_call", id = call.Id, name = call.Name, arguments = call.ArgumentsJson }, false, token).ConfigureAwait(false);

                        long toolStartMs = System.Diagnostics.Stopwatch.GetTimestamp();
                        ToolInvocationResult result = await ExecuteToolAsync(rc, call, subjectId, citedLinkScores, token).ConfigureAwait(false);
                        long toolDurationMs = (long)((System.Diagnostics.Stopwatch.GetTimestamp() - toolStartMs) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);

                        string resultJson = result.Success ? Json.Serialize(result.Result) : Json.Serialize(new { error = result.Error });
                        messages.Add(ChatMessage.ToolResult(call.Id ?? String.Empty, call.Name ?? String.Empty, resultJson));

                        toolTrace.Add(new { name = call.Name, ok = result.Success });
                        // The result payload is echoed to the caller (truncated) so the UI can show the tool's
                        // response alongside its query and runtime when a tool row is expanded.
                        await emit(new
                        {
                            type = "tool_result",
                            id = call.Id,
                            name = call.Name,
                            ok = result.Success,
                            error = result.Success ? null : result.Error,
                            result = Truncate(resultJson, 8192),
                            durationMs = toolDurationMs
                        }, false, token).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[AgenticChatService] chat loop error: " + e.Message);
                if (!producedText)
                {
                    await emit(new { type = "error", message = "The assistant encountered an error." }, true, token).ConfigureAwait(false);
                    return;
                }
            }

            if (String.IsNullOrEmpty(finalAnswer) && !producedText)
            {
                finalAnswer = "I wasn't able to complete the request.";
                await emit(new { type = "delta", text = finalAnswer }, false, token).ConfigureAwait(false);
            }

            List<object> citations = await ResolveCitationsAsync(tenantId, citedLinkScores, token).ConfigureAwait(false);

            double tokensPerSecond = generationMs > 0 && completionTokens > 0 ? completionTokens / (generationMs / 1000.0) : 0.0;
            await emit(new
            {
                type = "complete",
                answer = finalAnswer,
                model,
                promptTokens,
                completionTokens,
                totalTokens,
                timeToFirstTokenMs = timeToFirstTokenMs < 0 ? 0 : timeToFirstTokenMs,
                generationMs,
                tokensPerSecond,
                toolCalls = toolTrace,
                citations
            }, true, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<ToolInvocationResult> ExecuteToolAsync(RequestContext rc, ToolCall call, string? subjectId, IDictionary<string, double> citedLinkScores, CancellationToken token)
        {
            JsonElement arguments;
            try
            {
                string raw = String.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson;
                using (JsonDocument doc = JsonDocument.Parse(raw))
                {
                    arguments = doc.RootElement.Clone();
                }
            }
            catch (JsonException)
            {
                return ToolInvocationResult.Fail("Tool arguments were not valid JSON.");
            }

            return await _Tools.ExecuteAsync(rc, call.Name ?? String.Empty, arguments, subjectId, citedLinkScores, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolve the content links the assistant drew from into citation objects (id, url, title, score) by
        /// reading each link. Most relevant first. The score is normalized to [0, 1] so every surface can render
        /// it identically as a percentage. Best-effort and bounded so a broken link read never fails the response.
        /// </summary>
        private async Task<List<object>> ResolveCitationsAsync(string tenantId, IDictionary<string, double> linkScores, CancellationToken token)
        {
            List<object> citations = new List<object>();
            if (linkScores == null || linkScores.Count == 0 || String.IsNullOrEmpty(tenantId)) return citations;

            List<KeyValuePair<string, double>> ordered = new List<KeyValuePair<string, double>>(linkScores);
            ordered.Sort((KeyValuePair<string, double> a, KeyValuePair<string, double> b) => b.Value.CompareTo(a.Value));

            foreach (KeyValuePair<string, double> entry in ordered)
            {
                if (citations.Count >= 12) break;
                if (String.IsNullOrEmpty(entry.Key)) continue;
                try
                {
                    SubjectLink? link = await _Db.SubjectLinks.ReadAsync(tenantId, entry.Key, token).ConfigureAwait(false);
                    if (link == null || String.IsNullOrWhiteSpace(link.Url)) continue;
                    double score = Math.Clamp(entry.Value, 0.0, 1.0);
                    citations.Add(new { linkId = link.Id, url = link.Url, title = String.IsNullOrWhiteSpace(link.Title) ? link.Url : link.Title, score });
                }
                catch (Exception e)
                {
                    _Logging.Debug("[AgenticChatService] citation resolve failed for " + entry.Key + ": " + e.Message);
                }
            }

            return citations;
        }

        private static string Truncate(string value, int max)
        {
            if (String.IsNullOrEmpty(value)) return value ?? String.Empty;
            if (value.Length <= max) return value;
            return value.Substring(0, max) + "… (truncated)";
        }

        private string? DecryptKey(ModelRunner runner)
        {
            if (String.IsNullOrEmpty(runner.AuthMaterialEncrypted)) return null;
            try
            {
                return _Cipher.Decrypt(runner.AuthMaterialEncrypted);
            }
            catch (Exception)
            {
                return null;
            }
        }

        #endregion
    }
}
