namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using SyslogLogging;

    /// <summary>
    /// Cross-encoder reranker over the common HTTP <c>/rerank</c> contract shared by hosted and self-hosted
    /// rerankers (Cohere / Jina / Hugging Face TEI style): POST <c>{ model, query, documents, top_n }</c> and
    /// read back per-document relevance scores from a <c>results</c> array (or a bare array), tolerant of the
    /// <c>relevance_score</c> and <c>score</c> field spellings. Any failure returns null so the caller can fall
    /// back to LLM listwise reranking. The endpoint is a global system setting (configured in pneuma.json);
    /// subjects opt in per-subject via their reranker type.
    /// </summary>
    public class HttpCrossEncoderReranker : ICrossEncoderReranker, IDisposable
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions { PropertyNamingPolicy = null };

        private readonly string? _Endpoint;
        private readonly string? _Model;
        private readonly string? _ApiKey;
        private readonly LoggingModule _Logging;
        private readonly HttpClient _Http;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the reranker.</summary>
        /// <param name="endpoint">Full URL of the rerank endpoint (e.g. <c>https://host/v1/rerank</c>); null/empty disables it.</param>
        /// <param name="model">Rerank model name sent in the request body (may be null).</param>
        /// <param name="apiKey">Bearer API key (may be null).</param>
        /// <param name="logging">Logging module.</param>
        /// <param name="timeoutMilliseconds">Request timeout in milliseconds.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="logging"/> is null.</exception>
        public HttpCrossEncoderReranker(string? endpoint, string? model, string? apiKey, LoggingModule logging, int timeoutMilliseconds = 30000, HttpMessageHandler? handler = null)
        {
            _Endpoint = String.IsNullOrWhiteSpace(endpoint) ? null : endpoint.Trim();
            _Model = String.IsNullOrWhiteSpace(model) ? null : model;
            _ApiKey = String.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Http = handler != null ? new HttpClient(handler) : new HttpClient();
            _Http.Timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds < 1000 ? 1000 : timeoutMilliseconds);
        }

        #endregion

        #region Public-Members

        /// <inheritdoc />
        public bool IsConfigured { get { return _Endpoint != null; } }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<IReadOnlyList<double>?> ScoreAsync(string query, IReadOnlyList<string> passages, CancellationToken token = default)
        {
            if (_Endpoint == null || passages == null || passages.Count == 0) return null;

            List<string> documents = new List<string>(passages.Count);
            foreach (string passage in passages) documents.Add(passage ?? String.Empty);

            object body = new
            {
                model = _Model,
                query = query ?? String.Empty,
                documents = documents,
                top_n = documents.Count,
                return_documents = false
            };

            try
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _Endpoint))
                {
                    request.Content = new StringContent(JsonSerializer.Serialize(body, _RequestJson), Encoding.UTF8, "application/json");
                    if (_ApiKey != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _ApiKey);

                    using (HttpResponseMessage response = await _Http.SendAsync(request, token).ConfigureAwait(false))
                    {
                        string content = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            _Logging.Warn("[HttpCrossEncoderReranker] rerank endpoint returned " + (int)response.StatusCode + ": " + Truncate(content, 256));
                            return null;
                        }
                        return Parse(content, documents.Count);
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                _Logging.Warn("[HttpCrossEncoderReranker] rerank call failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Dispose the underlying HTTP client.</summary>
        public void Dispose()
        {
            _Http.Dispose();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Parse a rerank response into an index-aligned score array. Accepts either a top-level array of
        /// results or an object with a <c>results</c> (or <c>data</c>) array; each result carries an
        /// <c>index</c> and a relevance score under <c>relevance_score</c> or <c>score</c>.
        /// </summary>
        private static IReadOnlyList<double>? Parse(string content, int count)
        {
            if (String.IsNullOrWhiteSpace(content)) return null;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(content))
                {
                    JsonElement root = doc.RootElement;
                    JsonElement results;
                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        results = root;
                    }
                    else if (root.ValueKind == JsonValueKind.Object
                        && (TryGetArray(root, "results", out results) || TryGetArray(root, "data", out results)))
                    {
                        // results resolved by TryGetArray
                    }
                    else
                    {
                        return null;
                    }

                    double[] scores = new double[count];
                    bool any = false;
                    foreach (JsonElement item in results.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        if (!item.TryGetProperty("index", out JsonElement indexEl) || indexEl.ValueKind != JsonValueKind.Number) continue;
                        if (!indexEl.TryGetInt32(out int index) || index < 0 || index >= count) continue;

                        double score = 0.0;
                        if (item.TryGetProperty("relevance_score", out JsonElement rs) && rs.ValueKind == JsonValueKind.Number) score = rs.GetDouble();
                        else if (item.TryGetProperty("score", out JsonElement s) && s.ValueKind == JsonValueKind.Number) score = s.GetDouble();
                        scores[index] = score;
                        any = true;
                    }
                    return any ? scores : null;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool TryGetArray(JsonElement root, string name, out JsonElement array)
        {
            if (root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                array = value;
                return true;
            }
            array = default;
            return false;
        }

        private static string Truncate(string value, int max)
        {
            if (String.IsNullOrEmpty(value) || value.Length <= max) return value ?? String.Empty;
            return value.Substring(0, max);
        }

        #endregion
    }
}
