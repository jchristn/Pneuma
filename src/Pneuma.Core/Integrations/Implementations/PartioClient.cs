namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// HTTP client for Partio chunking, embedding, and summarization. Resolves and caches the
    /// embedding (and, when needed, completion) endpoint identifiers on first use.
    /// </summary>
    public class PartioClient : IntegrationClientBase, IPartioClient, IServiceProbe
    {
        #region Private-Members

        // Partio deserializes request bodies case-sensitively and expects PascalCase, so these must be
        // serialized WITHOUT the global camelCase policy (which would make Partio see the fields as missing).
        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string _BearerToken;
        private readonly string? _TenantId;
        private readonly SemaphoreSlim _ResolveLock = new SemaphoreSlim(1, 1);

        private string? _EmbeddingEndpointId;
        private string? _CompletionEndpointId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new Partio client.</summary>
        /// <param name="baseUrl">Base URL of the Partio service.</param>
        /// <param name="bearerToken">Bearer token for authentication.</param>
        /// <param name="embeddingEndpointId">Optional embedding endpoint id; resolved on first use when null.</param>
        /// <param name="completionEndpointId">Optional completion endpoint id; resolved on first summarize when null.</param>
        /// <param name="tenantId">Partio tenant that Pneuma-managed endpoints are created under; null omits the tenant from create bodies.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        public PartioClient(
            string baseUrl,
            string bearerToken,
            string? embeddingEndpointId,
            string? completionEndpointId,
            string? tenantId = null,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("partio", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken ?? String.Empty;
            _EmbeddingEndpointId = String.IsNullOrWhiteSpace(embeddingEndpointId) ? null : embeddingEndpointId;
            _CompletionEndpointId = String.IsNullOrWhiteSpace(completionEndpointId) ? null : completionEndpointId;
            _TenantId = String.IsNullOrWhiteSpace(tenantId) ? null : tenantId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default)
        {
            return ProbeResilientAsync("probe", () => new HttpRequestMessage(HttpMethod.Get, _BaseUrl + "/"), token);
        }

        /// <inheritdoc />
        public async Task<PartioProcessResult> ProcessAsync(string text, bool summarize, string? summarizationPrompt = null, string? embeddingEndpointId = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            string embeddingId = String.IsNullOrWhiteSpace(embeddingEndpointId)
                ? await ResolveEndpointAsync(false, token).ConfigureAwait(false)
                : embeddingEndpointId;
            string? completionId = null;
            if (summarize)
            {
                completionId = String.IsNullOrWhiteSpace(completionEndpointId)
                    ? await ResolveEndpointAsync(true, token).ConfigureAwait(false)
                    : completionEndpointId;
            }

            object requestBody;
            if (summarize)
            {
                object summarizationConfig = String.IsNullOrWhiteSpace(summarizationPrompt)
                    ? new { CompletionEndpointId = completionId, Order = "TopDown", MaxSummaryTokens = 1024 }
                    : (object)new { CompletionEndpointId = completionId, Order = "TopDown", MaxSummaryTokens = 1024, SummarizationPrompt = summarizationPrompt };

                requestBody = new
                {
                    Type = "Text",
                    Text = text,
                    ChunkingConfiguration = new { Strategy = "FixedTokenCount", FixedTokenCount = 256, OverlapCount = 32 },
                    EmbeddingConfiguration = new { EmbeddingEndpointId = embeddingId, L2Normalization = true },
                    SummarizationConfiguration = summarizationConfig
                };
            }
            else
            {
                requestBody = new
                {
                    Type = "Text",
                    Text = text,
                    ChunkingConfiguration = new { Strategy = "FixedTokenCount", FixedTokenCount = 256, OverlapCount = 32 },
                    EmbeddingConfiguration = new { EmbeddingEndpointId = embeddingId, L2Normalization = true }
                };
            }

            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/process", JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

            SemanticCellResponseDto? response = Json.Deserialize<SemanticCellResponseDto>(responseBody);
            PartioProcessResult result = new PartioProcessResult();
            if (response == null) return result;

            result.Chunks = MapChunks(response.Chunks);

            if (response.Children != null)
            {
                foreach (SemanticCellDto child in response.Children)
                {
                    if (child == null) continue;
                    if (String.Equals(child.Type, "Summary", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Summary = child.Text;
                        result.SummaryChunks = MapChunks(child.Chunks);
                        break;
                    }
                }
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<string> SummarizeAsync(string text, string? summarizationPrompt = null, string? completionEndpointId = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(text)) return String.Empty;

            string completionId = String.IsNullOrWhiteSpace(completionEndpointId)
                ? await ResolveEndpointAsync(true, token).ConfigureAwait(false)
                : completionEndpointId!;

            object summarizationConfig = String.IsNullOrWhiteSpace(summarizationPrompt)
                ? new { CompletionEndpointId = completionId, Order = "TopDown", MaxSummaryTokens = 1024 }
                : (object)new { CompletionEndpointId = completionId, Order = "TopDown", MaxSummaryTokens = 1024, SummarizationPrompt = summarizationPrompt };

            object requestBody = new { Text = text, SummarizationConfiguration = summarizationConfig };
            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/summarize", JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

            SummarizeResponseDto? response = Json.Deserialize<SummarizeResponseDto>(responseBody);
            return response?.Summary ?? String.Empty;
        }

        /// <inheritdoc />
        public async Task<List<PartioChunk>> ChunkAsync(string text, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(text)) return new List<PartioChunk>();

            object requestBody = new
            {
                Type = "Text",
                Text = text,
                ChunkingConfiguration = new { Strategy = "FixedTokenCount", FixedTokenCount = 256, OverlapCount = 32 }
            };
            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/chunk", JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

            SemanticCellResponseDto? response = Json.Deserialize<SemanticCellResponseDto>(responseBody);
            return MapChunks(response?.Chunks);
        }

        /// <inheritdoc />
        public async Task<List<List<float>>> EmbedAsync(List<string> texts, string? embeddingEndpointId = null, CancellationToken token = default)
        {
            List<List<float>> vectors = new List<List<float>>();
            if (texts == null || texts.Count == 0) return vectors;

            string embeddingId = String.IsNullOrWhiteSpace(embeddingEndpointId)
                ? await ResolveEndpointAsync(false, token).ConfigureAwait(false)
                : embeddingEndpointId!;

            object requestBody = new { EndpointId = embeddingId, Input = texts, L2Normalization = true };
            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/embed", JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

            EmbedResponseDto? response = Json.Deserialize<EmbedResponseDto>(responseBody);
            if (response?.Embeddings == null) return vectors;
            foreach (List<float>? vector in response.Embeddings)
            {
                vectors.Add(vector ?? new List<float>());
            }
            return vectors;
        }

        /// <inheritdoc />
        public async Task<List<PartioEndpoint>> ListEmbeddingEndpointsAsync(CancellationToken token = default)
        {
            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/endpoints/embedding/enumerate", "{}", token).ConfigureAwait(false);
            return ParseEndpoints(responseBody);
        }

        /// <inheritdoc />
        public async Task<List<PartioEndpoint>> ListCompletionEndpointsAsync(CancellationToken token = default)
        {
            string responseBody = await PostJsonAsync(_BaseUrl + "/v1.0/endpoints/completion/enumerate", "{}", token).ConfigureAwait(false);
            return ParseEndpoints(responseBody);
        }

        /// <inheritdoc />
        public async Task<PartioEndpoint?> ReadEndpointAsync(string type, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            string? body = await SendAsync(HttpMethod.Get, _BaseUrl + "/v1.0/endpoints/" + type + "/" + id, null, true, token).ConfigureAwait(false);
            if (body == null) return null;
            return ParseSingleEndpoint(body);
        }

        /// <inheritdoc />
        public async Task<PartioEndpoint> CreateEndpointAsync(string type, PartioEndpoint endpoint, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            string json = JsonSerializer.Serialize(BuildEndpointBody(endpoint, _TenantId), _RequestJson);
            string? body = await SendAsync(HttpMethod.Put, _BaseUrl + "/v1.0/endpoints/" + type, json, false, token).ConfigureAwait(false);
            return ParseSingleEndpoint(body ?? "{}") ?? endpoint;
        }

        /// <inheritdoc />
        public async Task<PartioEndpoint> UpdateEndpointAsync(string type, string id, PartioEndpoint endpoint, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            if (endpoint == null) throw new ArgumentNullException(nameof(endpoint));
            string json = JsonSerializer.Serialize(BuildEndpointBody(endpoint, _TenantId), _RequestJson);
            string? body = await SendAsync(HttpMethod.Put, _BaseUrl + "/v1.0/endpoints/" + type + "/" + id, json, false, token).ConfigureAwait(false);
            return ParseSingleEndpoint(body ?? "{}") ?? endpoint;
        }

        /// <inheritdoc />
        public async Task DeleteEndpointAsync(string type, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(type)) throw new ArgumentNullException(nameof(type));
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            await SendAsync(HttpMethod.Delete, _BaseUrl + "/v1.0/endpoints/" + type + "/" + id, null, false, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private static List<PartioEndpoint> ParseEndpoints(string json)
        {
            List<PartioEndpoint> endpoints = new List<PartioEndpoint>();
            if (String.IsNullOrWhiteSpace(json)) return endpoints;

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement array = FindEndpointArray(doc.RootElement);
                if (array.ValueKind != JsonValueKind.Array) return endpoints;

                foreach (JsonElement item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string? id = GetStringProperty(item, "Id", "id");
                    if (String.IsNullOrEmpty(id)) continue;

                    bool active = false;
                    if (TryGetProperty(item, out JsonElement activeEl, "Active", "active") && activeEl.ValueKind == JsonValueKind.True)
                    {
                        active = true;
                    }

                    endpoints.Add(new PartioEndpoint
                    {
                        Id = id,
                        Name = GetStringProperty(item, "Name", "name"),
                        Model = GetStringProperty(item, "Model", "model"),
                        ApiFormat = GetStringProperty(item, "ApiFormat", "apiFormat"),
                        Endpoint = GetStringProperty(item, "Endpoint", "endpoint"),
                        Active = active,
                        MaxConcurrentRequests = GetIntProperty(item, 2, "MaxConcurrentRequests", "maxConcurrentRequests"),
                        MaxQueueDepth = GetIntProperty(item, 0, "MaxQueueDepth", "maxQueueDepth"),
                        ContextSize = GetTagInt(item, "contextSize")
                    });
                }
            }

            return endpoints;
        }

        private static PartioEndpoint MapEndpoint(JsonElement item)
        {
            bool active = false;
            if (TryGetProperty(item, out JsonElement activeEl, "Active", "active") && activeEl.ValueKind == JsonValueKind.True) active = true;
            return new PartioEndpoint
            {
                Id = GetStringProperty(item, "Id", "id") ?? String.Empty,
                Name = GetStringProperty(item, "Name", "name"),
                Model = GetStringProperty(item, "Model", "model"),
                ApiFormat = GetStringProperty(item, "ApiFormat", "apiFormat"),
                Endpoint = GetStringProperty(item, "Endpoint", "endpoint"),
                Active = active,
                MaxConcurrentRequests = GetIntProperty(item, 2, "MaxConcurrentRequests", "maxConcurrentRequests"),
                MaxQueueDepth = GetIntProperty(item, 0, "MaxQueueDepth", "maxQueueDepth"),
                ContextSize = GetTagInt(item, "contextSize")
            };
        }

        private static PartioEndpoint? ParseSingleEndpoint(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;
            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return null;
                // The endpoint may be returned bare or wrapped under a "Data" property.
                if (!TryGetProperty(root, out JsonElement _, "Id", "id") && TryGetProperty(root, out JsonElement data, "Data", "data") && data.ValueKind == JsonValueKind.Object)
                {
                    root = data;
                }
                if (String.IsNullOrEmpty(GetStringProperty(root, "Id", "id"))) return null;
                return MapEndpoint(root);
            }
        }

        private static object BuildEndpointBody(PartioEndpoint endpoint, string? tenantId)
        {
            // Partio has no discrete context-window field, so the model's context size round-trips via the
            // endpoint's extensible Tags. Only emit the tag when a positive value is set.
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            if (endpoint.ContextSize > 0) tags["contextSize"] = endpoint.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture);

            return new
            {
                TenantId = String.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
                Name = endpoint.Name,
                Model = endpoint.Model,
                Endpoint = endpoint.Endpoint,
                ApiFormat = endpoint.ApiFormat,
                ApiKey = endpoint.ApiKey,
                Active = endpoint.Active,
                MaxConcurrentRequests = Math.Max(1, endpoint.MaxConcurrentRequests),
                MaxQueueDepth = Math.Max(0, endpoint.MaxQueueDepth),
                Tags = tags
            };
        }

        private async Task<string?> SendAsync(HttpMethod method, string url, string? json, bool nullOn404, CancellationToken token)
        {
            bool isWrite = method == HttpMethod.Put || method == HttpMethod.Delete;
            IntegrationResponse response = await SendRawResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(method, url, json),
                isWrite,
                token).ConfigureAwait(false);

            if (nullOn404 && response.StatusCode == (int)System.Net.HttpStatusCode.NotFound) return null;
            if (!response.IsSuccess)
            {
                throw new IntegrationClientException("partio", RouteNormalizer.NormalizeUrl(url), response.StatusCode, Truncate(response.Body, 512));
            }

            return response.Body;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, string? json)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, url);
            if (json != null) request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!String.IsNullOrEmpty(_BearerToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _BearerToken);
            }
            return request;
        }

        private async Task<string> ResolveEndpointAsync(bool completion, CancellationToken token)
        {
            if (completion && !String.IsNullOrEmpty(_CompletionEndpointId)) return _CompletionEndpointId!;
            if (!completion && !String.IsNullOrEmpty(_EmbeddingEndpointId)) return _EmbeddingEndpointId!;

            await _ResolveLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (completion && !String.IsNullOrEmpty(_CompletionEndpointId)) return _CompletionEndpointId!;
                if (!completion && !String.IsNullOrEmpty(_EmbeddingEndpointId)) return _EmbeddingEndpointId!;

                string url = completion
                    ? _BaseUrl + "/v1.0/endpoints/completion/enumerate"
                    : _BaseUrl + "/v1.0/endpoints/embedding/enumerate";

                string responseBody = await PostJsonAsync(url, "{}", token).ConfigureAwait(false);
                string? resolved = ExtractFirstEndpointId(responseBody);
                if (String.IsNullOrEmpty(resolved))
                {
                    throw new InvalidOperationException("Unable to resolve Partio " + (completion ? "completion" : "embedding") + " endpoint id from enumerate response.");
                }

                if (completion) _CompletionEndpointId = resolved;
                else _EmbeddingEndpointId = resolved;
                return resolved!;
            }
            finally
            {
                _ResolveLock.Release();
            }
        }

        private static string? ExtractFirstEndpointId(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) return null;

            using (JsonDocument doc = JsonDocument.Parse(json))
            {
                JsonElement array = FindEndpointArray(doc.RootElement);
                if (array.ValueKind != JsonValueKind.Array) return null;

                string? firstId = null;
                foreach (JsonElement item in array.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    string? id = GetStringProperty(item, "Id", "id");
                    if (String.IsNullOrEmpty(id)) continue;
                    if (firstId == null) firstId = id;

                    bool active = false;
                    if (TryGetProperty(item, out JsonElement activeEl, "Active", "active") && activeEl.ValueKind == JsonValueKind.True)
                    {
                        active = true;
                    }
                    if (active) return id;
                }

                return firstId;
            }
        }

        private static JsonElement FindEndpointArray(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in new[] { "Objects", "Data", "Endpoints" })
                {
                    if (TryGetProperty(root, out JsonElement named, name) && named.ValueKind == JsonValueKind.Array)
                    {
                        return named;
                    }
                }

                // Fall back to the first array of objects that contains an id field.
                foreach (JsonProperty prop in root.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement el in prop.Value.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.Object &&
                                (TryGetProperty(el, out _, "Id", "id")))
                            {
                                return prop.Value;
                            }
                            break;
                        }
                    }
                }
            }

            return default;
        }

        private static List<PartioChunk> MapChunks(List<ChunkDto>? chunks)
        {
            List<PartioChunk> result = new List<PartioChunk>();
            if (chunks == null) return result;
            foreach (ChunkDto chunk in chunks)
            {
                if (chunk == null) continue;
                result.Add(new PartioChunk
                {
                    Text = chunk.Text ?? String.Empty,
                    Embeddings = chunk.Embeddings ?? new List<float>()
                });
            }
            return result;
        }

        private async Task<string> PostJsonAsync(string url, string json, CancellationToken token)
        {
            return await SendResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(HttpMethod.Post, url, json),
                isWrite: false,
                token).ConfigureAwait(false);
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
                {
                    return true;
                }
            }
            value = default;
            return false;
        }

        private static string? GetStringProperty(JsonElement element, params string[] names)
        {
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        private static int GetIntProperty(JsonElement element, int fallback, params string[] names)
        {
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed))
            {
                return parsed;
            }
            return fallback;
        }

        /// <summary>Read an integer value stored under a key in the endpoint's Tags object; 0 when absent/invalid.</summary>
        private static int GetTagInt(JsonElement element, string tagKey)
        {
            if (!TryGetProperty(element, out JsonElement tags, "Tags", "tags") || tags.ValueKind != JsonValueKind.Object) return 0;
            if (!tags.TryGetProperty(tagKey, out JsonElement value)) return 0;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number)) return number;
            if (value.ValueKind == JsonValueKind.String && Int32.TryParse(value.GetString(), out int parsed)) return parsed;
            return 0;
        }

        #endregion

        #region Private-Types

        private class SemanticCellResponseDto
        {
            public List<ChunkDto>? Chunks { get; set; }
            public List<SemanticCellDto>? Children { get; set; }
        }

        private class SemanticCellDto
        {
            public string? Type { get; set; }
            public string? Text { get; set; }
            public List<ChunkDto>? Chunks { get; set; }
        }

        private class ChunkDto
        {
            public string? Text { get; set; }
            public List<float>? Embeddings { get; set; }
        }

        private class EmbedResponseDto
        {
            public List<List<float>?>? Embeddings { get; set; }
        }

        private class SummarizeResponseDto
        {
            public string? Summary { get; set; }
        }

        #endregion
    }
}
