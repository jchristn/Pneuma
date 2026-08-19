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
    /// HTTP client for Verbex indexing and search. Verbex wraps payloads in an envelope whose
    /// content lives under a top-level "Data" property.
    /// </summary>
    public class VerbexClient : IntegrationClientBase, IVerbexClient, IServiceProbe
    {
        #region Private-Members

        // Verbex deserializes request bodies case-sensitively and expects PascalCase, so request
        // bodies must be serialized WITHOUT the global camelCase policy (else fields like "Name" are
        // seen as missing).
        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string _BearerToken;
        private readonly string _TenantId;
        private readonly string _IndexName;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new Verbex client.</summary>
        /// <param name="baseUrl">Base URL of the Verbex service.</param>
        /// <param name="bearerToken">Bearer token for authentication.</param>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="indexName">Name of the index to ensure and use.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        public VerbexClient(
            string baseUrl,
            string bearerToken,
            string tenantId,
            string indexName,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("verbex", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken ?? String.Empty;
            _TenantId = tenantId ?? String.Empty;
            _IndexName = indexName ?? String.Empty;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default)
        {
            return ProbeResilientAsync("probe", () => new HttpRequestMessage(HttpMethod.Get, _BaseUrl + "/"), token);
        }

        /// <inheritdoc />
        public async Task<string> EnsureIndexAsync(CancellationToken token = default)
        {
            string listBody = await SendAsync(HttpMethod.Get, _BaseUrl + "/v1.0/indices", null, token).ConfigureAwait(false);

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(listBody) ? "{}" : listBody))
            {
                JsonElement data = GetDataElement(doc.RootElement);
                JsonElement array = FindArray(data);
                if (array.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement index in array.EnumerateArray())
                    {
                        string? name = GetStringProperty(index, "Name", "name");
                        if (name != null && String.Equals(name, _IndexName, StringComparison.Ordinal))
                        {
                            string? id = GetStringProperty(index, "Identifier", "identifier", "Id", "id");
                            if (!String.IsNullOrEmpty(id)) return id!;
                        }
                    }
                }
            }

            string createBody = await SendAsync(HttpMethod.Post, _BaseUrl + "/v1.0/indices",
                JsonSerializer.Serialize(new { Name = _IndexName, TenantId = _TenantId }, _RequestJson), token, isWrite: true).ConfigureAwait(false);

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(createBody) ? "{}" : createBody))
            {
                JsonElement data = GetDataElement(doc.RootElement);

                if (TryGetProperty(data, out JsonElement inner, "Index", "index"))
                {
                    string? nestedId = GetStringProperty(inner, "Identifier", "identifier", "Id", "id");
                    if (!String.IsNullOrEmpty(nestedId)) return nestedId!;
                }

                string? id = GetStringProperty(data, "Identifier", "identifier", "Id", "id");
                if (!String.IsNullOrEmpty(id)) return id!;
            }

            throw new HttpRequestException("Verbex index creation did not return an identifier.");
        }

        /// <inheritdoc />
        public async Task<string> AddDocumentAsync(string indexId, string content, Dictionary<string, string> tags, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(indexId)) throw new ArgumentNullException(nameof(indexId));
            Dictionary<string, string> safeTags = tags ?? new Dictionary<string, string>();

            string body = await SendAsync(HttpMethod.Post, _BaseUrl + "/v1.0/indices/" + indexId + "/documents",
                JsonSerializer.Serialize(new { Content = content, Tags = safeTags, CustomMetadata = safeTags }, _RequestJson), token, isWrite: true).ConfigureAwait(false);

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
            {
                JsonElement data = GetDataElement(doc.RootElement);
                string? id = GetStringProperty(data, "DocumentId", "documentId", "Id", "id");
                if (!String.IsNullOrEmpty(id)) return id!;
            }

            throw new HttpRequestException("Verbex document creation did not return an identifier.");
        }

        /// <inheritdoc />
        public Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, CancellationToken token = default)
        {
            return SearchAsync(indexId, query, maxResults, null, token);
        }

        /// <inheritdoc />
        public async Task<List<VerbexHit>> SearchAsync(string indexId, string query, int maxResults, Dictionary<string, string>? tags, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(indexId)) throw new ArgumentNullException(nameof(indexId));

            object requestBody = (tags != null && tags.Count > 0)
                ? new { Query = query, MaxResults = maxResults, Tags = tags }
                : (object)new { Query = query, MaxResults = maxResults };
            string body = await SendAsync(HttpMethod.Post, _BaseUrl + "/v1.0/indices/" + indexId + "/search",
                JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

            List<VerbexHit> hits = new List<VerbexHit>();

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
            {
                JsonElement data = GetDataElement(doc.RootElement);
                if (!TryGetProperty(data, out JsonElement results, "Results", "results") || results.ValueKind != JsonValueKind.Array)
                {
                    return hits;
                }

                foreach (JsonElement result in results.EnumerateArray())
                {
                    if (result.ValueKind != JsonValueKind.Object) continue;

                    VerbexHit hit = new VerbexHit
                    {
                        DocumentId = GetStringProperty(result, "DocumentId", "documentId", "Id", "id") ?? String.Empty,
                        Score = GetDoubleProperty(result, "Score", "score"),
                        Snippet = GetStringProperty(result, "Snippet", "snippet", "Highlight", "highlight")
                    };

                    if (TryGetProperty(result, out JsonElement document, "Document", "document") && document.ValueKind == JsonValueKind.Object)
                    {
                        Dictionary<string, string>? docTags = GetStringMap(document, "Tags", "tags");
                        if (docTags == null || docTags.Count == 0)
                        {
                            docTags = GetStringMap(document, "CustomMetadata", "customMetadata");
                        }
                        if (docTags != null) hit.Tags = docTags;

                        if (String.IsNullOrEmpty(hit.Snippet))
                        {
                            string? content = GetStringProperty(document, "Content", "content", "Text", "text");
                            if (!String.IsNullOrEmpty(content))
                            {
                                hit.Snippet = content!.Length > 300 ? content.Substring(0, 300) + "…" : content;
                            }
                        }
                    }

                    hits.Add(hit);
                }
            }

            return hits;
        }

        /// <inheritdoc />
        public async Task DeleteDocumentsAsync(IEnumerable<string> documentIds, CancellationToken token = default)
        {
            if (documentIds == null) return;

            string indexId = await EnsureIndexAsync(token).ConfigureAwait(false);
            foreach (string documentId in documentIds)
            {
                if (String.IsNullOrWhiteSpace(documentId)) continue;
                // Best-effort per document: an already-absent document must not abort the wider cascade.
                try
                {
                    await SendAsync(HttpMethod.Delete, _BaseUrl + "/v1.0/indices/" + indexId + "/documents/" + documentId, null, token, isWrite: true).ConfigureAwait(false);
                }
                catch
                {
                    // Swallow — the document may already be gone; deletion is idempotent.
                }
            }
        }

        #endregion

        #region Private-Methods

        private async Task<string> SendAsync(HttpMethod method, string url, string? json, CancellationToken token, bool isWrite = false)
        {
            return await SendResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () =>
                {
                    HttpRequestMessage request = new HttpRequestMessage(method, url);
                    if (json != null)
                    {
                        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    }
                    if (!String.IsNullOrEmpty(_BearerToken))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _BearerToken);
                    }
                    return request;
                },
                isWrite,
                token).ConfigureAwait(false);
        }

        private static JsonElement GetDataElement(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Data", out JsonElement data))
            {
                if (data.ValueKind != JsonValueKind.Null && data.ValueKind != JsonValueKind.Undefined) return data;
            }
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out JsonElement dataLower))
            {
                if (dataLower.ValueKind != JsonValueKind.Null && dataLower.ValueKind != JsonValueKind.Undefined) return dataLower;
            }
            return root;
        }

        private static JsonElement FindArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Array) return element;

            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in new[] { "Indices", "Indexes", "Objects", "Data", "Results" })
                {
                    if (TryGetProperty(element, out JsonElement named, name) && named.ValueKind == JsonValueKind.Array)
                    {
                        return named;
                    }
                }
                foreach (JsonProperty prop in element.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array) return prop.Value;
                }
            }

            return default;
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in names)
                {
                    if (element.TryGetProperty(name, out value)) return true;
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

        private static double GetDoubleProperty(JsonElement element, params string[] names)
        {
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d))
            {
                return d;
            }
            return 0;
        }

        private static Dictionary<string, string>? GetStringMap(JsonElement element, params string[] names)
        {
            if (!TryGetProperty(element, out JsonElement map, names) || map.ValueKind != JsonValueKind.Object) return null;

            Dictionary<string, string> result = new Dictionary<string, string>();
            foreach (JsonProperty prop in map.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    result[prop.Name] = prop.Value.GetString() ?? String.Empty;
                }
                else if (prop.Value.ValueKind != JsonValueKind.Null && prop.Value.ValueKind != JsonValueKind.Undefined)
                {
                    result[prop.Name] = prop.Value.GetRawText();
                }
            }
            return result;
        }

        #endregion
    }
}
