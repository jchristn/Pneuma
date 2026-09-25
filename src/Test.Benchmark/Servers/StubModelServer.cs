namespace Test.Benchmark.Servers
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Models;
    using WatsonWebserver;
    using WatsonWebserver.Core;

    /// <summary>
    /// A deterministic model server with a fixed, configurable latency, speaking the Ollama and OpenAI formats.
    /// <list type="bullet">
    /// <item>Embeddings (<c>/api/embed</c>, <c>/api/embeddings</c>, <c>/v1/embeddings</c>) come from feature hashing
    /// over lower-cased word tokens, so texts that share words get similar vectors. Load tests use it to measure
    /// Pneuma and RecallDB rather than the embedding model.</item>
    /// <item>Chat (<c>/api/chat</c>, <c>/v1/chat/completions</c>) returns an empty knowledge-graph subgraph for
    /// ontology-classification prompts and an empty reply otherwise, so the <c>lean</c> ingest profile runs the
    /// whole pipeline without an LLM: no entities are extracted and no cell summaries are indexed.</item>
    /// </list>
    /// </summary>
    public class StubModelServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL the server listens on.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Embedding requests served.
        /// </summary>
        public long EmbeddingRequests
        {
            get
            {
                return Interlocked.Read(ref _EmbeddingRequests);
            }
        }

        /// <summary>
        /// Chat requests served.
        /// </summary>
        public long ChatRequests
        {
            get
            {
                return Interlocked.Read(ref _ChatRequests);
            }
        }

        /// <summary>
        /// The reply sent to classification prompts.
        /// </summary>
        public const string EmptySubgraph = "{\"nodes\":[],\"edges\":[]}";

        #endregion

        #region Private-Members

        private readonly int _Dimensionality;
        private readonly int _LatencyMs;
        private readonly Webserver _Server;
        private long _EmbeddingRequests = 0;
        private long _ChatRequests = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate and start listening on the loopback interface.
        /// </summary>
        /// <param name="port">Port.</param>
        /// <param name="dimensionality">Embedding dimensionality.</param>
        /// <param name="latencyMs">Added latency per request.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the dimensionality is below 1.</exception>
        public StubModelServer(int port, int dimensionality, int latencyMs)
        {
            if (dimensionality < 1) throw new ArgumentOutOfRangeException(nameof(dimensionality));
            _Dimensionality = dimensionality;
            _LatencyMs = Math.Max(0, latencyMs);
            BaseUrl = "http://127.0.0.1:" + port;
            WebserverSettings settings = new WebserverSettings("127.0.0.1", port, false);
            _Server = new Webserver(settings, RouteAsync);
            _Server.Start();
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Compute the stub embedding for a text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <returns>An L2-normalized vector.</returns>
        public float[] Embed(string text)
        {
            float[] vector = new float[_Dimensionality];
            int start = -1;
            string lower = (text ?? string.Empty).ToLowerInvariant();
            for (int i = 0; i <= lower.Length; i++)
            {
                bool wordChar = i < lower.Length && char.IsLetterOrDigit(lower[i]);
                if (wordChar && start < 0) start = i;
                if (!wordChar && start >= 0)
                {
                    uint hash = Fnv(lower, start, i - start);
                    int index = (int)(hash % (uint)_Dimensionality);
                    vector[index] += (hash & 0x80000000u) != 0 ? -1.0f : 1.0f;
                    start = -1;
                }
            }

            double norm = 0.0;
            foreach (float v in vector) norm += v * v;
            if (norm <= 0.0)
            {
                vector[0] = 1.0f;
                return vector;
            }

            float scale = (float)(1.0 / Math.Sqrt(norm));
            for (int i = 0; i < vector.Length; i++) vector[i] *= scale;
            return vector;
        }

        /// <summary>
        /// The stub's reply to a chat conversation.
        /// </summary>
        /// <param name="messages">The conversation.</param>
        /// <returns>The reply text.</returns>
        public static string Reply(List<ChatWireMessage> messages)
        {
            foreach (ChatWireMessage message in messages)
            {
                if (message.Content != null && message.Content.IndexOf("Respond with ONLY a JSON object", StringComparison.Ordinal) >= 0) return EmptySubgraph;
            }

            return string.Empty;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task RouteAsync(HttpContextBase ctx)
        {
            string path = ctx.Request.Url.RawWithoutQuery ?? "/";
            string method = ctx.Request.Method.ToString().ToUpperInvariant();
            try
            {
                if (method == "GET")
                {
                    if (path == "/api/tags") await WriteAsync(ctx, "{\"models\":[{\"name\":\"stub\",\"model\":\"stub\"}]}").ConfigureAwait(false);
                    else if (path == "/api/version") await WriteAsync(ctx, "{\"version\":\"stub\"}").ConfigureAwait(false);
                    else await WriteAsync(ctx, "{\"status\":\"stub model server ok\"}").ConfigureAwait(false);
                    return;
                }

                string body = ctx.Request.DataAsString ?? string.Empty;
                if (path == "/api/embed" || path == "/api/embeddings")
                {
                    OllamaEmbedRequest request = JsonSerializer.Deserialize<OllamaEmbedRequest>(body) ?? new OllamaEmbedRequest();
                    await DelayAsync(true).ConfigureAwait(false);
                    OllamaEmbedResponse response = new OllamaEmbedResponse { Model = request.Model };
                    if (path == "/api/embeddings")
                    {
                        response.Embedding = Embed(request.Prompt ?? (request.Input != null && request.Input.Count > 0 ? request.Input[0] : string.Empty));
                    }
                    else
                    {
                        response.Embeddings = new List<float[]>();
                        foreach (string input in request.Input ?? new List<string>()) response.Embeddings.Add(Embed(input));
                    }

                    await WriteAsync(ctx, JsonSerializer.Serialize(response)).ConfigureAwait(false);
                    return;
                }

                if (path == "/v1/embeddings" || path == "/embeddings")
                {
                    OpenAiEmbedRequest request = JsonSerializer.Deserialize<OpenAiEmbedRequest>(body) ?? new OpenAiEmbedRequest();
                    await DelayAsync(true).ConfigureAwait(false);
                    OpenAiEmbedResponse response = new OpenAiEmbedResponse { Model = string.IsNullOrEmpty(request.Model) ? "stub" : request.Model };
                    for (int i = 0; i < request.Input.Count; i++) response.Data.Add(new OpenAiEmbedding { Index = i, Embedding = Embed(request.Input[i]) });
                    await WriteAsync(ctx, JsonSerializer.Serialize(response)).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/chat")
                {
                    OllamaChatRequest request = JsonSerializer.Deserialize<OllamaChatRequest>(body) ?? new OllamaChatRequest();
                    await DelayAsync(false).ConfigureAwait(false);
                    OllamaChatResponse response = new OllamaChatResponse
                    {
                        Model = request.Model,
                        CreatedAt = DateTime.UtcNow.ToString("o"),
                        Message = new ChatWireMessage { Role = "assistant", Content = Reply(request.Messages) },
                        Done = true,
                        DoneReason = "stop",
                        PromptEvalCount = 1,
                        EvalCount = 1
                    };

                    // A streamed request gets the whole reply as its single, final NDJSON line.
                    string json = JsonSerializer.Serialize(response);
                    if (request.Stream != false)
                    {
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "application/x-ndjson";
                        await ctx.Response.Send(json + "\n").ConfigureAwait(false);
                        return;
                    }

                    await WriteAsync(ctx, json).ConfigureAwait(false);
                    return;
                }

                if (path == "/v1/chat/completions" || path == "/chat/completions")
                {
                    OpenAiChatRequest request = JsonSerializer.Deserialize<OpenAiChatRequest>(body) ?? new OpenAiChatRequest();
                    await DelayAsync(false).ConfigureAwait(false);
                    string reply = Reply(request.Messages);
                    if (request.Stream == true)
                    {
                        OpenAiChatResponse chunk = new OpenAiChatResponse { Object = "chat.completion.chunk", Model = request.Model };
                        chunk.Choices.Add(new OpenAiChoice { Index = 0, Delta = new ChatWireMessage { Role = "assistant", Content = reply }, FinishReason = "stop" });
                        StringBuilder sse = new StringBuilder();
                        sse.Append("data: ").Append(JsonSerializer.Serialize(chunk)).Append("\n\n").Append("data: [DONE]\n\n");
                        ctx.Response.StatusCode = 200;
                        ctx.Response.ContentType = "text/event-stream";
                        await ctx.Response.Send(sse.ToString()).ConfigureAwait(false);
                        return;
                    }

                    OpenAiChatResponse response = new OpenAiChatResponse { Model = request.Model };
                    response.Choices.Add(new OpenAiChoice { Index = 0, Message = new ChatWireMessage { Role = "assistant", Content = reply }, FinishReason = "stop" });
                    await WriteAsync(ctx, JsonSerializer.Serialize(response)).ConfigureAwait(false);
                    return;
                }

                ctx.Response.StatusCode = 404;
                await WriteAsync(ctx, "{\"error\":\"not found\"}").ConfigureAwait(false);
            }
            catch (JsonException e)
            {
                ctx.Response.StatusCode = 400;
                await WriteAsync(ctx, JsonSerializer.Serialize(new Dictionary<string, string> { { "error", e.Message } })).ConfigureAwait(false);
            }
        }

        private async Task DelayAsync(bool embedding)
        {
            if (embedding) Interlocked.Increment(ref _EmbeddingRequests);
            else Interlocked.Increment(ref _ChatRequests);
            if (_LatencyMs > 0) await Task.Delay(_LatencyMs).ConfigureAwait(false);
        }

        private static async Task WriteAsync(HttpContextBase ctx, string json)
        {
            if (ctx.Response.StatusCode == 0) ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.Send(json).ConfigureAwait(false);
        }

        private static uint Fnv(string text, int start, int length)
        {
            uint hash = 2166136261u;
            for (int i = start; i < start + length; i++)
            {
                hash ^= text[i];
                hash *= 16777619u;
            }

            return hash;
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                try
                {
                    _Server.Stop();
                }
                catch (ObjectDisposedException)
                {
                }

                _Server.Dispose();
            }

            _Disposed = true;
        }

        #endregion
    }
}
