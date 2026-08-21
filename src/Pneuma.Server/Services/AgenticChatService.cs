namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Text;
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

        // Fraction of the model's context window at which the running conversation is compacted. Leaves head-
        // room for tool definitions, retrieved content, and the answer itself.
        private const double _CompactionThreshold = 0.7;

        private const string _FallbackCompressionPrompt =
            "Compact the conversation so far into a single, self-contained summary. Preserve every important detail " +
            "(the user's goals, constraints, decisions, facts, named entities, and unresolved questions), deduplicate " +
            "repeated information, and aggressively minimize length. Do not invent anything or answer the latest " +
            "question. Output only the summary.";

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

            // Subject context drives the per-subject system prompt (global base + subject appended), the answer
            // model, whether model thinking is surfaced, and the optional prompt-rewrite / rerank steps.
            Subject? subject = String.IsNullOrWhiteSpace(subjectId)
                ? null
                : await _Db.Subjects.ReadAsync(tenantId, subjectId!, token).ConfigureAwait(false);

            ModelRunner? runner = await _Query.ResolveAnswerRunnerAsync(tenantId, subject, token).ConfigureAwait(false);
            if (runner == null)
            {
                await emit(new { type = "complete", answer = "No answering model is configured. Ask an administrator to add a model runner.", model = (string?)null }, true, token).ConfigureAwait(false);
                return;
            }

            Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(tenantId, "assistant.system", token).ConfigureAwait(false);
            string systemPrompt = String.IsNullOrWhiteSpace(prompt?.Content) ? _FallbackSystemPrompt : prompt!.Content;
            if (subject != null && !String.IsNullOrWhiteSpace(subject.SystemPrompt))
            {
                systemPrompt = systemPrompt + "\n\n" + subject.SystemPrompt!.Trim();
            }
            bool thinkingEnabled = subject?.ThinkingEnabled ?? false;

            string? apiKey = DecryptKey(runner);
            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);
            List<ToolDefinition> tools = McpToolCatalog.BuildAssistantToolDefinitions();

            // Optional prompt-rewrite of the latest user turn: the model then reasons and forms its tool queries
            // over the rewritten question. The original turn is unchanged in the caller's transcript.
            int lastUserIndex = -1;
            for (int i = turns.Count - 1; i >= 0; i--)
            {
                if (!String.Equals(turns[i].Role, "assistant", StringComparison.OrdinalIgnoreCase)) { lastUserIndex = i; break; }
            }
            // Per-stage performance telemetry for this turn (rewrite, compaction, tool calls, final inference).
            List<TurnPerformanceStage> perfStages = new List<TurnPerformanceStage>();
            long rewriteStart = System.Diagnostics.Stopwatch.GetTimestamp();
            string? rewrittenLastTurn = lastUserIndex >= 0
                ? await _Query.RewriteQuestionAsync(tenantId, subject, turns[lastUserIndex].Content ?? String.Empty, token).ConfigureAwait(false)
                : null;
            if (subject != null && !String.IsNullOrWhiteSpace(subject.PromptRewriteModel))
            {
                perfStages.Add(new TurnPerformanceStage { Name = "prompt_rewrite", Kind = "inference", DurationMs = ElapsedMs(rewriteStart) });
            }

            List<ChatMessage> messages = new List<ChatMessage>();
            messages.Add(ChatMessage.System(systemPrompt));
            for (int i = 0; i < turns.Count; i++)
            {
                ChatTurn turn = turns[i];
                string content = turn.Content ?? String.Empty;
                if (String.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)) messages.Add(ChatMessage.Assistant(content));
                else messages.Add(ChatMessage.User(i == lastUserIndex && rewrittenLastTurn != null ? rewrittenLastTurn : content));
            }

            // Automatic conversation compaction: when the running history approaches the answering model's
            // context window, summarize everything before the latest user turn and replace it, so the
            // conversation can continue within budget. The summary is streamed back so the caller can replace
            // its own message history with it.
            string? compactedSummary = null;
            if (runner.ContextSize > 0 && turns.Count > 2)
            {
                int budget = (int)(runner.ContextSize * _CompactionThreshold);
                if (EstimateTokens(messages) > budget)
                {
                    await emit(new { type = "compacting" }, false, token).ConfigureAwait(false);
                    long compactStart = System.Diagnostics.Stopwatch.GetTimestamp();
                    compactedSummary = await CompactAsync(tenantId, client, turns, token).ConfigureAwait(false);
                    perfStages.Add(new TurnPerformanceStage { Name = "compaction", Kind = "inference", DurationMs = ElapsedMs(compactStart) });
                    if (!String.IsNullOrWhiteSpace(compactedSummary))
                    {
                        messages = new List<ChatMessage>
                        {
                            ChatMessage.System(systemPrompt),
                            ChatMessage.Assistant("Summary of the conversation so far:\n" + compactedSummary),
                            ChatMessage.User(turns[turns.Count - 1].Content ?? String.Empty)
                        };
                    }
                }
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

            // Model reasoning arrives inline as <think>...</think>. Strip it from the live delta stream and
            // capture it (plus its duration) separately so it is never mixed into the answer; the subject's
            // thinking flag governs whether the caller renders it.
            ThinkParser thinkParser = new ThinkParser();

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
                            string visible = thinkParser.Feed(chunk.Text);
                            if (!String.IsNullOrEmpty(visible))
                            {
                                producedText = true;
                                await emit(new { type = "delta", text = visible }, false, token).ConfigureAwait(false);
                            }
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
                        finalAnswer = ThinkParser.Strip(response.Text ?? String.Empty).Trim();
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
                        perfStages.Add(new TurnPerformanceStage { Name = "tool:" + (call.Name ?? "unknown"), Kind = "tool", DurationMs = toolDurationMs, Success = result.Success });
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

            // Flush any visible text held back for partial-marker detection.
            string flushed = thinkParser.Finish();
            if (!String.IsNullOrEmpty(flushed))
            {
                producedText = true;
                await emit(new { type = "delta", text = flushed }, false, token).ConfigureAwait(false);
                if (String.IsNullOrEmpty(finalAnswer)) finalAnswer = flushed.Trim();
            }

            if (String.IsNullOrEmpty(finalAnswer) && !producedText)
            {
                finalAnswer = "I wasn't able to complete the request.";
                await emit(new { type = "delta", text = finalAnswer }, false, token).ConfigureAwait(false);
            }

            List<object> citations = await ResolveCitationsAsync(tenantId, citedLinkScores, token).ConfigureAwait(false);

            // The final inference stage carries the accumulated generation timing and tokens for this turn.
            perfStages.Add(new TurnPerformanceStage
            {
                Name = "final_inference",
                Kind = "inference",
                Provider = runner.Provider.ToString(),
                Model = model,
                DurationMs = generationMs,
                TimeToFirstTokenMs = timeToFirstTokenMs < 0 ? 0 : timeToFirstTokenMs,
                PromptTokens = (int)promptTokens,
                CompletionTokens = (int)completionTokens
            });
            double wallMs = 0;
            foreach (TurnPerformanceStage stage in perfStages) wallMs += stage.DurationMs;
            TurnPerformance performance = new TurnPerformance { SchemaVersion = 1, WallTimeMs = wallMs, Stages = perfStages };

            // Build the turn record up front so its id can be handed to the caller (for feedback) in the
            // complete event; it is persisted immediately after.
            ChatTurnRecord record = new ChatTurnRecord
            {
                TenantId = tenantId,
                SubjectId = String.IsNullOrWhiteSpace(subjectId) ? null : subjectId,
                UserId = rc.UserId,
                Question = turns.Count > 0 ? (turns[turns.Count - 1].Content ?? String.Empty) : String.Empty,
                Answer = finalAnswer,
                Thinking = String.IsNullOrWhiteSpace(thinkParser.Thinking) ? null : thinkParser.Thinking,
                Model = model,
                PromptTokens = (int)promptTokens,
                CompletionTokens = (int)completionTokens,
                TotalTokens = (int)totalTokens,
                TimeToFirstTokenMs = timeToFirstTokenMs < 0 ? 0 : timeToFirstTokenMs,
                GenerationMs = generationMs,
                ThinkingMs = thinkParser.ThinkingMs,
                ContextSize = runner.ContextSize,
                CitationsJson = citations.Count > 0 ? Json.Serialize(citations) : null,
                PerformanceJson = Json.Serialize(performance),
                PerformanceSchemaVersion = performance.SchemaVersion
            };

            double tokensPerSecond = generationMs > 0 && completionTokens > 0 ? completionTokens / (generationMs / 1000.0) : 0.0;
            await emit(new
            {
                type = "complete",
                turnId = record.Id,
                answer = finalAnswer,
                model,
                promptTokens,
                completionTokens,
                totalTokens,
                timeToFirstTokenMs = timeToFirstTokenMs < 0 ? 0 : timeToFirstTokenMs,
                generationMs,
                tokensPerSecond,
                contextSize = runner.ContextSize,
                toolCalls = toolTrace,
                citations,
                thinking = thinkParser.Thinking,
                thinkingMs = thinkParser.ThinkingMs,
                thinkingEnabled,
                compacted = !String.IsNullOrWhiteSpace(compactedSummary),
                compactedSummary
            }, true, token).ConfigureAwait(false);

            // Persist the completed turn for the History/Feedback surfaces, then opportunistically prune this
            // subject's history beyond its retention window. Best-effort: a persistence failure never fails the answer.
            try
            {
                await _Db.ChatTurns.CreateAsync(record, token).ConfigureAwait(false);

                List<ChatTurnPerfEvent> perfEvents = new List<ChatTurnPerfEvent>();
                foreach (TurnPerformanceStage stage in perfStages)
                {
                    perfEvents.Add(new ChatTurnPerfEvent
                    {
                        TenantId = tenantId,
                        TurnId = record.Id,
                        SubjectId = record.SubjectId,
                        Stage = stage.Name,
                        Kind = stage.Kind,
                        Provider = stage.Provider,
                        Model = stage.Model,
                        DurationMs = stage.DurationMs,
                        TimeToFirstTokenMs = stage.TimeToFirstTokenMs,
                        PromptTokens = stage.PromptTokens,
                        CompletionTokens = stage.CompletionTokens,
                        Success = stage.Success
                    });
                }
                await _Db.ChatTurnPerfEvents.CreateManyAsync(perfEvents, token).ConfigureAwait(false);

                if (subject != null && subject.HistoryRetentionDays > 0)
                {
                    DateTime cutoff = DateTime.UtcNow.AddDays(-subject.HistoryRetentionDays);
                    await _Db.ChatTurns.DeleteOlderThanAsync(tenantId, subject.Id, cutoff, token).ConfigureAwait(false);
                    await _Db.ChatTurnPerfEvents.DeleteOlderThanAsync(tenantId, subject.Id, cutoff, token).ConfigureAwait(false);
                }
            }
            catch (Exception persistError)
            {
                _Logging.Warn("[AgenticChatService] failed to persist chat turn: " + persistError.Message);
            }
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

        /// <summary>Elapsed milliseconds since a <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> reading.</summary>
        /// <param name="startTimestamp">A prior high-resolution timestamp.</param>
        /// <returns>Elapsed milliseconds.</returns>
        private static double ElapsedMs(long startTimestamp)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }

        /// <summary>Rough token estimate for the running conversation (≈4 characters per token).</summary>
        private static int EstimateTokens(List<ChatMessage> messages)
        {
            long chars = 0;
            foreach (ChatMessage message in messages)
            {
                if (!String.IsNullOrEmpty(message.Content)) chars += message.Content.Length;
            }
            return (int)Math.Min(int.MaxValue, chars / 4);
        }

        /// <summary>
        /// Summarize the conversation up to (but excluding) the latest user turn using the tenant's configured
        /// compression prompt, so the prior history can be replaced by a compact summary. Best-effort: returns
        /// null when the model produces nothing usable.
        /// </summary>
        private async Task<string?> CompactAsync(string tenantId, CompletionClientBase client, List<ChatTurn> turns, CancellationToken token)
        {
            Prompt? prompt = await _Db.Prompts.ReadByKeyAsync(tenantId, "assistant.compress", token).ConfigureAwait(false);
            string compressionPrompt = String.IsNullOrWhiteSpace(prompt?.Content) ? _FallbackCompressionPrompt : prompt!.Content;

            StringBuilder transcript = new StringBuilder();
            for (int i = 0; i < turns.Count - 1; i++)
            {
                ChatTurn turn = turns[i];
                string role = String.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User";
                transcript.Append(role).Append(": ").AppendLine(turn.Content ?? String.Empty);
            }
            if (transcript.Length == 0) return null;

            try
            {
                ChatCompletionOptions options = new ChatCompletionOptions
                {
                    Temperature = 0.2,
                    MaxTokens = 1024,
                    SystemPrompt = compressionPrompt
                };
                ChatResponse response = await client.ChatAsync(transcript.ToString(), options, token).ConfigureAwait(false);
                if (response != null && response.Success && !String.IsNullOrWhiteSpace(response.Text)) return response.Text.Trim();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[AgenticChatService] conversation compaction failed: " + e.Message);
            }
            return null;
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

        #region Private-Types

        /// <summary>
        /// Incremental splitter that separates model reasoning delimited by <c>&lt;think&gt;</c>/<c>&lt;/think&gt;</c>
        /// from the visible answer as text streams in. Markers may straddle chunk boundaries, so a short tail is
        /// held back until it can be classified. Also accumulates the total time spent inside think blocks.
        /// </summary>
        private sealed class ThinkParser
        {
            private const string _Open = "<think>";
            private const string _Close = "</think>";

            private bool _Inside = false;
            private string _Pending = String.Empty;
            private readonly System.Text.StringBuilder _Thinking = new System.Text.StringBuilder();
            private long _ThinkingMs = 0;
            private long _ThinkStartTicks = 0;

            /// <summary>The accumulated reasoning text, trimmed.</summary>
            public string Thinking { get { return _Thinking.ToString().Trim(); } }

            /// <summary>Total milliseconds spent inside think blocks.</summary>
            public long ThinkingMs { get { return _ThinkingMs; } }

            /// <summary>Feed a streamed text fragment; returns the visible (non-thinking) portion to emit.</summary>
            /// <param name="text">The incoming fragment.</param>
            /// <returns>Visible text safe to stream to the caller.</returns>
            public string Feed(string text)
            {
                _Pending += text ?? String.Empty;
                System.Text.StringBuilder visible = new System.Text.StringBuilder();

                while (_Pending.Length > 0)
                {
                    if (!_Inside)
                    {
                        int i = _Pending.IndexOf(_Open, StringComparison.Ordinal);
                        if (i >= 0)
                        {
                            visible.Append(_Pending, 0, i);
                            _Pending = _Pending.Substring(i + _Open.Length);
                            _Inside = true;
                            _ThinkStartTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        }
                        else
                        {
                            int keep = PartialTailLength(_Pending, _Open);
                            visible.Append(_Pending, 0, _Pending.Length - keep);
                            _Pending = _Pending.Substring(_Pending.Length - keep);
                            break;
                        }
                    }
                    else
                    {
                        int j = _Pending.IndexOf(_Close, StringComparison.Ordinal);
                        if (j >= 0)
                        {
                            _Thinking.Append(_Pending, 0, j);
                            _Pending = _Pending.Substring(j + _Close.Length);
                            _Inside = false;
                            _ThinkingMs += ElapsedMs(_ThinkStartTicks);
                        }
                        else
                        {
                            int keep = PartialTailLength(_Pending, _Close);
                            _Thinking.Append(_Pending, 0, _Pending.Length - keep);
                            _Pending = _Pending.Substring(_Pending.Length - keep);
                            break;
                        }
                    }
                }

                return visible.ToString();
            }

            /// <summary>Flush any held-back text at end of stream. Returns trailing visible text (empty if the
            /// stream ended mid-think, in which case the remainder is folded into the captured reasoning).</summary>
            /// <returns>Trailing visible text.</returns>
            public string Finish()
            {
                string tail = _Pending;
                _Pending = String.Empty;
                if (_Inside)
                {
                    _Thinking.Append(tail);
                    _ThinkingMs += ElapsedMs(_ThinkStartTicks);
                    _Inside = false;
                    return String.Empty;
                }
                return tail;
            }

            /// <summary>Strip all think blocks from a complete text (non-streaming).</summary>
            /// <param name="text">Full text.</param>
            /// <returns>Text with reasoning removed.</returns>
            public static string Strip(string text)
            {
                if (String.IsNullOrEmpty(text)) return text ?? String.Empty;
                ThinkParser parser = new ThinkParser();
                string visible = parser.Feed(text);
                return visible + parser.Finish();
            }

            private static long ElapsedMs(long startTicks)
            {
                return (long)((System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            }

            // Longest suffix of s that is a proper prefix of marker, so a marker split across chunk boundaries
            // is not emitted prematurely.
            private static int PartialTailLength(string s, string marker)
            {
                int max = Math.Min(s.Length, marker.Length - 1);
                for (int len = max; len > 0; len--)
                {
                    if (String.CompareOrdinal(s, s.Length - len, marker, 0, len) == 0) return len;
                }
                return 0;
            }
        }

        #endregion
    }
}
