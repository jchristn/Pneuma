namespace Pneuma.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Models;
    using Pneuma.Core.Responses;
    using Pneuma.Core.Security;
    using PolyPrompt.Clients;
    using PolyPrompt.Models;
    using SyslogLogging;

    /// <summary>
    /// Actively validates a model endpoint by exercising the exact runtime path it serves. A completion
    /// endpoint is checked with a basic completion and a tool-calling round-trip (the same tool-capable,
    /// streaming call agentic chat makes — the path that silently fails when a model or endpoint does not
    /// support function calling). An embedding endpoint is checked with a live embedding request through
    /// Partio. Each probe is time-bounded so a wedged upstream fails cleanly rather than hanging the request.
    /// </summary>
    public class ModelRunnerValidationService
    {
        #region Private-Members

        // Per-probe wall-clock ceiling: a wedged upstream fails as a timed-out check instead of hanging the request.
        // Reasoning models (e.g. gpt-oss:20b) can spend many seconds "thinking" before emitting a token, so this
        // is generous enough that a genuinely working reasoning endpoint is not misreported as a timeout.
        private static readonly TimeSpan _ProbeTimeout = TimeSpan.FromSeconds(60);

        private readonly IPartioClient _Partio;
        private readonly GroundedQueryService _Query;
        private readonly Aes256Cipher _Cipher;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the model runner validation service.</summary>
        /// <param name="partio">Partio client (endpoint lookup and embedding probe).</param>
        /// <param name="query">Grounded query service (resolves a Partio completion endpoint into a transient runner).</param>
        /// <param name="cipher">Cipher for decrypting the resolved runner's auth material.</param>
        /// <param name="logging">Logging module.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public ModelRunnerValidationService(IPartioClient partio, GroundedQueryService query, Aes256Cipher cipher, LoggingModule logging)
        {
            if (partio == null) throw new ArgumentNullException(nameof(partio));
            if (query == null) throw new ArgumentNullException(nameof(query));
            if (cipher == null) throw new ArgumentNullException(nameof(cipher));
            if (logging == null) throw new ArgumentNullException(nameof(logging));
            _Partio = partio;
            _Query = query;
            _Cipher = cipher;
            _Logging = logging;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a model endpoint by id, exercising the completion (and tool-calling) or embedding path per
        /// its type. Returns null when no endpoint with the given id exists.
        /// </summary>
        /// <param name="endpointId">Partio endpoint id (completion or embedding).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The validation result, or null when the endpoint is not found.</returns>
        public async Task<ModelEndpointValidationDto?> ValidateAsync(string endpointId, string? type, CancellationToken token)
        {
            if (String.IsNullOrWhiteSpace(endpointId)) return null;

            try
            {
                // The endpoint type must be honored when supplied: Partio seeds the default embedding and default
                // completion endpoints with the SAME id ("default"), so probing completion-first would validate the
                // wrong endpoint for the default embedding. Only fall back to probing both when type is unknown.
                bool wantCompletion = String.Equals(type, "completion", StringComparison.OrdinalIgnoreCase);
                bool wantEmbedding = String.Equals(type, "embedding", StringComparison.OrdinalIgnoreCase);

                if (wantCompletion || !wantEmbedding)
                {
                    PartioEndpoint? completion = await _Partio.ReadEndpointAsync("completion", endpointId, token).ConfigureAwait(false);
                    if (completion != null) return await ValidateCompletionAsync(completion, token).ConfigureAwait(false);
                    if (wantCompletion) return null;
                }

                PartioEndpoint? embedding = await _Partio.ReadEndpointAsync("embedding", endpointId, token).ConfigureAwait(false);
                if (embedding != null) return await ValidateEmbeddingAsync(embedding, token).ConfigureAwait(false);

                return null;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // An error while resolving or probing the endpoint (including upstream/provider errors such as a
                // bad Authorization header) should read as a failed validation with the real reason, not a broken
                // "validation could not be run" for the whole request.
                _Logging.Warn("[ModelRunnerValidationService] validation of endpoint " + endpointId + " failed: " + e.Message);
                return new ModelEndpointValidationDto
                {
                    EndpointId = endpointId,
                    Type = "Unknown",
                    CheckedUtc = DateTime.UtcNow,
                    Ok = false,
                    Checks = new List<ModelEndpointValidationCheck>
                    {
                        new ModelEndpointValidationCheck { Name = "Validate", Ok = false, Error = e.Message }
                    }
                };
            }
        }

        #endregion

        #region Private-Methods

        private async Task<ModelEndpointValidationDto> ValidateCompletionAsync(PartioEndpoint endpoint, CancellationToken token)
        {
            ModelEndpointValidationDto result = NewResult(endpoint, "Completion");

            ModelRunner? runner = await _Query.ResolveCompletionRunnerByIdAsync(endpoint.Id, token).ConfigureAwait(false);
            if (runner == null)
            {
                result.Ok = false;
                result.Checks.Add(new ModelEndpointValidationCheck
                {
                    Name = "Resolve endpoint",
                    Ok = false,
                    Error = "The completion endpoint could not be resolved into a runnable model configuration."
                });
                return result;
            }

            string? apiKey = DecryptKey(runner);
            CompletionClientBase client = ModelClientFactory.Create(runner, apiKey, _Logging);

            ModelEndpointValidationCheck completionCheck = await RunCompletionProbeAsync(client, token).ConfigureAwait(false);
            result.Checks.Add(completionCheck);

            ModelEndpointValidationCheck toolCheck = await RunToolCallingProbeAsync(client, token).ConfigureAwait(false);
            result.Checks.Add(toolCheck);

            // Tool calling is an informational capability probe, not a requirement: many completion models
            // (used for summarization or plain completion) do not support tools, and that does not make the
            // endpoint invalid. Only the completion check gates the overall result.
            result.Ok = completionCheck.Ok;
            return result;
        }

        private async Task<ModelEndpointValidationDto> ValidateEmbeddingAsync(PartioEndpoint endpoint, CancellationToken token)
        {
            ModelEndpointValidationDto result = NewResult(endpoint, "Embedding");
            ModelEndpointValidationCheck check = await RunEmbeddingProbeAsync(endpoint.Id, token).ConfigureAwait(false);
            result.Checks.Add(check);
            result.Ok = check.Ok;
            return result;
        }

        private async Task<ModelEndpointValidationCheck> RunCompletionProbeAsync(CompletionClientBase client, CancellationToken token)
        {
            ModelEndpointValidationCheck check = new ModelEndpointValidationCheck { Name = "Completion" };
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (CancellationTokenSource cts = LinkedTimeout(token))
            {
                try
                {
                    ChatCompletionOptions options = new ChatCompletionOptions
                    {
                        Temperature = 0.0,
                        // Reasoning models (e.g. gpt-oss:20b) spend their token budget "thinking" before emitting a
                        // visible answer, so a tiny cap gets fully consumed by reasoning and returns empty visible
                        // text. Give enough budget to both reason and answer.
                        MaxTokens = 512,
                        SystemPrompt = "You are a health probe. Reply with exactly the single word: OK"
                    };
                    ChatResponse response = await client.ChatAsync("Reply with exactly: OK", options, cts.Token).ConfigureAwait(false);
                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;

                    // A reasoning model spends tokens "thinking" (often in a <think> block) before its visible
                    // answer, so the probe passes on ANY generated output — reasoning or visible text — not just a
                    // non-empty final answer. rawText includes any think block, so it is non-empty whenever the
                    // model produced anything at all.
                    string rawText = response?.Text ?? String.Empty;
                    string visibleText = ThinkStrip(rawText);

                    if (response != null && response.Success && !String.IsNullOrWhiteSpace(rawText))
                    {
                        check.Ok = true;
                        check.Detail = !String.IsNullOrWhiteSpace(visibleText)
                            ? "Model replied: " + Trim(visibleText, 120)
                            : "Model produced reasoning output (no visible text) — the endpoint responds.";
                    }
                    else
                    {
                        check.Ok = false;
                        check.Error = response == null ? "No response from the model." : (response.Error ?? "The model returned an empty completion.");
                    }
                }
                catch (Exception e)
                {
                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                    check.Ok = false;
                    check.Error = DescribeException(e, token, cts.Token);
                }
            }
            return check;
        }

        private async Task<ModelEndpointValidationCheck> RunToolCallingProbeAsync(CompletionClientBase client, CancellationToken token)
        {
            ModelEndpointValidationCheck check = new ModelEndpointValidationCheck { Name = "Tool calling" };
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (CancellationTokenSource cts = LinkedTimeout(token))
            {
                try
                {
                    ToolChatRequest request = new ToolChatRequest
                    {
                        Messages = new List<ChatMessage>
                        {
                            ChatMessage.System("You are a health probe. Call the get_time tool to answer."),
                            ChatMessage.User("What time is it? Use the get_time tool.")
                        },
                        Tools = new List<ToolDefinition> { BuildProbeTool() },
                        ToolChoice = "auto",
                        Temperature = 0.0,
                        // Headroom so a reasoning model can think before emitting the tool call.
                        MaxTokens = 512
                    };

                    ToolChatStreamingResponse response = await client.ToolChatStreamingAsync(request, cts.Token).ConfigureAwait(false);
                    if (response == null || !response.Success)
                    {
                        // A rejected tool request (e.g. HTTP 400 "does not support tools") means the model simply
                        // has no tool-calling capability — informational, not a validation failure.
                        check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                        MarkToolUnsupported(check, response == null ? "no response from the model" : (response.Error ?? "the request was rejected"));
                        return check;
                    }

                    // Drain the stream fully; only then are the aggregate fields (ToolCalls, text) populated.
                    System.Text.StringBuilder text = new System.Text.StringBuilder();
                    await foreach (ToolChatStreamingChunk chunk in response.Chunks.WithCancellation(cts.Token).ConfigureAwait(false))
                    {
                        if (!String.IsNullOrEmpty(chunk.Text)) text.Append(chunk.Text);
                    }

                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                    List<ToolCall> calls = response.ToolCalls ?? new List<ToolCall>();
                    if (calls.Count > 0)
                    {
                        check.Ok = true;
                        check.Detail = "Model requested tool '" + (calls[0].Name ?? "unknown") + "' — function calling is supported.";
                    }
                    else
                    {
                        // A successful stream that answers in prose without ever calling the offered tool means the
                        // model does not honor function calling. That is fine for completion/summarization use;
                        // report it as a capability warning rather than a failure.
                        MarkToolUnsupported(check, "the model answered without calling the offered tool");
                    }
                }
                catch (Exception e)
                {
                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                    MarkToolUnsupported(check, DescribeException(e, token, cts.Token));
                }
            }
            return check;
        }

        private async Task<ModelEndpointValidationCheck> RunEmbeddingProbeAsync(string endpointId, CancellationToken token)
        {
            ModelEndpointValidationCheck check = new ModelEndpointValidationCheck { Name = "Embedding" };
            Stopwatch stopwatch = Stopwatch.StartNew();
            using (CancellationTokenSource cts = LinkedTimeout(token))
            {
                try
                {
                    List<List<float>> vectors = await _Partio.EmbedAsync(new List<string> { "Pneuma model endpoint validation probe." }, endpointId, cts.Token).ConfigureAwait(false);
                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                    if (vectors != null && vectors.Count > 0 && vectors[0] != null && vectors[0].Count > 0)
                    {
                        check.Ok = true;
                        check.Detail = "Returned a " + vectors[0].Count + "-dimensional embedding vector.";
                    }
                    else
                    {
                        check.Ok = false;
                        check.Error = "The embedding request returned no vector.";
                    }
                }
                catch (Exception e)
                {
                    check.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                    check.Ok = false;
                    check.Error = DescribeException(e, token, cts.Token);
                }
            }
            return check;
        }

        // Tool calling is a capability, not a requirement: record its absence as a non-failing warning so an
        // endpoint that only does plain completion/summarization still validates as healthy.
        private static void MarkToolUnsupported(ModelEndpointValidationCheck check, string reason)
        {
            check.Ok = false;
            check.Warning = true;
            check.Error = null;
            check.Detail = "Tool calling not supported (" + Trim(reason, 160)
                + "). This endpoint can serve completions and summarization, but not agentic (tool-using) chat.";
        }

        private static ModelEndpointValidationDto NewResult(PartioEndpoint endpoint, string type)
        {
            return new ModelEndpointValidationDto
            {
                EndpointId = endpoint.Id,
                Name = endpoint.Name,
                Type = type,
                Model = endpoint.Model,
                Endpoint = endpoint.Endpoint,
                ApiFormat = endpoint.ApiFormat,
                CheckedUtc = DateTime.UtcNow
            };
        }

        private static ToolDefinition BuildProbeTool()
        {
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>
                {
                    ["timezone"] = new Dictionary<string, object>
                    {
                        ["type"] = "string",
                        ["description"] = "Optional IANA timezone name."
                    }
                }
            };
            return ToolDefinition.Function("get_time", "Return the current time. A trivial probe tool used to confirm the endpoint honors function calling.", schema);
        }

        private CancellationTokenSource LinkedTimeout(CancellationToken token)
        {
            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(token);
            cts.CancelAfter(_ProbeTimeout);
            return cts;
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

        // Distinguish a probe timeout from a caller-initiated cancellation from a genuine upstream error.
        private static string DescribeException(Exception e, CancellationToken callerToken, CancellationToken probeToken)
        {
            if (e is OperationCanceledException)
            {
                if (callerToken.IsCancellationRequested) return "The request was cancelled.";
                if (probeToken.IsCancellationRequested) return "The probe timed out (the endpoint did not respond within 30 seconds).";
            }
            return e.Message;
        }

        private static string ThinkStrip(string text)
        {
            // Local models may wrap reasoning in <think>...</think>; keep only the visible tail for the detail line.
            if (String.IsNullOrEmpty(text)) return String.Empty;
            int close = text.LastIndexOf("</think>", StringComparison.Ordinal);
            string visible = close >= 0 ? text.Substring(close + "</think>".Length) : text;
            return visible.Trim();
        }

        private static string Trim(string value, int max)
        {
            if (String.IsNullOrEmpty(value)) return String.Empty;
            value = value.Trim();
            return value.Length <= max ? value : value.Substring(0, max) + "…";
        }

        #endregion
    }
}
