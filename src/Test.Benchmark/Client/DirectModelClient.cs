namespace Test.Benchmark.Client
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Test.Benchmark.Models;

    /// <summary>
    /// Calls an embedding or chat model directly (never through Pneuma), in the Ollama or OpenAI wire format. The
    /// LLM judge and the reference retrieval arm use it, so neither shares a code path with the system under test.
    /// </summary>
    public class DirectModelClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// ollama or openai.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Base URL.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Model name.
        /// </summary>
        public string Model { get; }

        /// <summary>
        /// A one-line description for reports.
        /// </summary>
        public string Description
        {
            get
            {
                return Format + ":" + Model + " @ " + BaseUrl;
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpClient _Http;
        private readonly string? _ApiKey;
        private readonly string _Think;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="format">ollama or openai.</param>
        /// <param name="baseUrl">Base URL (for openai, the URL that /v1/... hangs off).</param>
        /// <param name="model">Model name.</param>
        /// <param name="apiKey">Optional bearer key.</param>
        /// <param name="timeout">Per-request timeout.</param>
        /// <param name="think">Ollama reasoning control for completions: false (default), or low, medium, high.</param>
        /// <exception cref="ArgumentException">Thrown for an unknown format.</exception>
        public DirectModelClient(string format, string baseUrl, string model, string? apiKey, TimeSpan timeout, string think = "false")
        {
            _Think = string.IsNullOrWhiteSpace(think) ? "false" : think;
            Format = (format ?? "ollama").ToLowerInvariant();
            if (Format != "ollama" && Format != "openai") throw new ArgumentException("Model format must be ollama or openai (got '" + format + "').");
            BaseUrl = (baseUrl ?? throw new ArgumentNullException(nameof(baseUrl))).TrimEnd('/');
            Model = model ?? throw new ArgumentNullException(nameof(model));
            _ApiKey = apiKey;
            _Http = new HttpClient { Timeout = timeout };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Embed texts, in batches.
        /// </summary>
        /// <param name="texts">Texts.</param>
        /// <param name="batchSize">Texts per request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>One vector per text.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the endpoint returns an unusable response.</exception>
        public async Task<List<float[]>> EmbedAsync(IReadOnlyList<string> texts, int batchSize, CancellationToken token)
        {
            List<float[]> vectors = new List<float[]>(texts.Count);
            for (int start = 0; start < texts.Count; start += batchSize)
            {
                List<string> batch = new List<string>();
                for (int i = start; i < Math.Min(texts.Count, start + batchSize); i++) batch.Add(texts[i]);
                string path;
                object body;
                if (Format == "ollama")
                {
                    path = "/api/embed";
                    body = new OllamaEmbedRequest { Model = Model, Input = batch };
                }
                else
                {
                    path = "/v1/embeddings";
                    body = new OpenAiEmbedRequest { Model = Model, Input = batch };
                }

                string text = await PostWithRetryAsync(path, body, token).ConfigureAwait(false);
                if (Format == "ollama")
                {
                    OllamaEmbedResponse? parsed = JsonSerializer.Deserialize<OllamaEmbedResponse>(text);
                    if (parsed?.Embeddings == null || parsed.Embeddings.Count != batch.Count) throw new InvalidOperationException("Embedding endpoint returned an unexpected response: " + Truncate(text));
                    vectors.AddRange(parsed.Embeddings);
                }
                else
                {
                    OpenAiEmbedResponse? parsed = JsonSerializer.Deserialize<OpenAiEmbedResponse>(text);
                    if (parsed?.Data == null || parsed.Data.Count != batch.Count) throw new InvalidOperationException("Embedding endpoint returned an unexpected response: " + Truncate(text));
                    parsed.Data.Sort((OpenAiEmbedding a, OpenAiEmbedding b) => a.Index.CompareTo(b.Index));
                    foreach (OpenAiEmbedding item in parsed.Data) vectors.Add(item.Embedding);
                }
            }

            return vectors;
        }

        /// <summary>
        /// Run a single-turn completion with thinking disabled where supported.
        /// </summary>
        /// <param name="systemPrompt">Optional system prompt.</param>
        /// <param name="userText">User message.</param>
        /// <param name="maxTokens">Maximum tokens.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The reply text.</returns>
        public async Task<string> CompleteAsync(string? systemPrompt, string userText, int maxTokens, CancellationToken token)
        {
            List<ChatWireMessage> messages = new List<ChatWireMessage>();
            if (!string.IsNullOrWhiteSpace(systemPrompt)) messages.Add(new ChatWireMessage { Role = "system", Content = systemPrompt });
            messages.Add(new ChatWireMessage { Role = "user", Content = userText });

            if (Format == "ollama")
            {
                OllamaChatRequest request = new OllamaChatRequest
                {
                    Model = Model,
                    Messages = messages,
                    Stream = false,
                    Think = _Think,
                    Options = new OllamaChatOptions { Temperature = 0.0, NumPredict = maxTokens }
                };
                string text = await PostWithRetryAsync("/api/chat", request, token).ConfigureAwait(false);
                OllamaChatResponse? parsed = JsonSerializer.Deserialize<OllamaChatResponse>(text);
                return parsed?.Message?.Content ?? string.Empty;
            }
            else
            {
                OpenAiChatRequest request = new OpenAiChatRequest { Model = Model, Messages = messages, Temperature = 0.0, MaxTokens = maxTokens, Stream = false };
                string text = await PostWithRetryAsync("/v1/chat/completions", request, token).ConfigureAwait(false);
                OpenAiChatResponse? parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(text);
                return parsed != null && parsed.Choices.Count > 0 ? parsed.Choices[0].Message?.Content ?? string.Empty : string.Empty;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private async Task<string> PostWithRetryAsync(string path, object body, CancellationToken token)
        {
            string json = JsonSerializer.Serialize(body, body.GetType());
            // Shared model gateways answer 429 ("at capacity") under load; retry those and 5xx with exponential
            // backoff (2, 4, 8, 16, 30, 30, 30 s) before giving up.
            Exception? last = null;
            const int maxAttempts = 8;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + path))
                    {
                        if (!string.IsNullOrEmpty(_ApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _ApiKey);
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                        using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                        {
                            string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                            if (response.IsSuccessStatusCode) return text;
                            last = new InvalidOperationException("HTTP " + (int)response.StatusCode + " from " + BaseUrl + path + ": " + Truncate(text));
                            if ((int)response.StatusCode < 500 && (int)response.StatusCode != 429) break;
                        }
                    }
                }
                catch (HttpRequestException e)
                {
                    last = e;
                }
                catch (TaskCanceledException e) when (!token.IsCancellationRequested)
                {
                    last = e;
                }

                if (attempt < maxAttempts) await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), token).ConfigureAwait(false);
            }

            throw new InvalidOperationException("Model call to " + BaseUrl + path + " failed: " + (last?.Message ?? "unknown error"), last);
        }

        private static string Truncate(string text)
        {
            return text.Length > 300 ? text.Substring(0, 300) + "..." : text;
        }

        /// <summary>
        /// Dispose resources.
        /// </summary>
        /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Http.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
