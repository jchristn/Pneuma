namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;
    using Pneuma.Core.Observability;

    /// <summary>
    /// HTTP client for RecallDB — the retrieval store that replaces both the lexical index and the
    /// graph-backed vector store. Backs <see cref="ICollectionStore"/> (collection administration),
    /// <see cref="IVectorRepository"/> (chunk storage + vector search), and <see cref="IInvertedIndex"/>
    /// (full-text search) against a single tenant/collection model. Request bodies are PascalCase (RecallDB
    /// is case-sensitive on write); responses are read case-insensitively.
    /// </summary>
    public class RecallDbClient : IntegrationClientBase, ICollectionStore, IVectorRepository, IInvertedIndex, IServiceProbe
    {
        #region Private-Members

        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string _BearerToken;
        private readonly string _TenantId;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new RecallDB client.</summary>
        /// <param name="baseUrl">Base URL of the RecallDB service.</param>
        /// <param name="bearerToken">Bearer token (admin API key or credential token).</param>
        /// <param name="tenantId">RecallDB tenant that Pneuma operates under.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="baseUrl"/> is null/empty.</exception>
        public RecallDbClient(
            string baseUrl,
            string bearerToken,
            string tenantId,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("recalldb", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken ?? String.Empty;
            _TenantId = String.IsNullOrWhiteSpace(tenantId) ? "default" : tenantId;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default)
        {
            return ProbeResilientAsync("probe", () => BuildRequest(HttpMethod.Get, _BaseUrl + "/", null), token);
        }

        /// <inheritdoc />
        public async Task<List<RecallCollection>> ListCollectionsAsync(string tenantId, CancellationToken token = default)
        {
            string body = await PostOrGetAsync(HttpMethod.Get, CollectionsUrl(tenantId), null, token).ConfigureAwait(false);
            List<RecallCollection> collections = new List<RecallCollection>();
            if (String.IsNullOrWhiteSpace(body)) return collections;
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                JsonElement array = FindArray(doc.RootElement, "Objects", "Data", "Collections");
                if (array.ValueKind != JsonValueKind.Array) return collections;
                foreach (JsonElement item in array.EnumerateArray())
                {
                    RecallCollection? collection = MapCollection(item);
                    if (collection != null) collections.Add(collection);
                }
            }
            return collections;
        }

        /// <inheritdoc />
        public async Task<RecallCollection?> ReadCollectionAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return null;
            string? body = await SendAsync(HttpMethod.Get, CollectionsUrl(tenantId) + "/" + id, null, true, token).ConfigureAwait(false);
            if (body == null) return null;
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                JsonElement root = Unwrap(doc.RootElement);
                return MapCollection(root);
            }
        }

        /// <inheritdoc />
        public async Task<RecallCollection> CreateCollectionAsync(string tenantId, RecallCollection collection, CancellationToken token = default)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            // Let RecallDB assign the collection id (it is the authority) unless one is explicitly supplied.
            object requestBody = String.IsNullOrWhiteSpace(collection.Id)
                ? (object)new { Name = collection.Name, Description = collection.Description, Dimensionality = collection.Dimensionality, Active = collection.Active }
                : new { Id = collection.Id, Name = collection.Name, Description = collection.Description, Dimensionality = collection.Dimensionality, Active = collection.Active };
            string json = JsonSerializer.Serialize(requestBody, _RequestJson);
            string? body = await SendAsync(HttpMethod.Put, CollectionsUrl(tenantId), json, false, token).ConfigureAwait(false);
            if (String.IsNullOrWhiteSpace(body)) return collection;
            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                RecallCollection? created = MapCollection(Unwrap(doc.RootElement));
                return created ?? collection;
            }
        }

        /// <inheritdoc />
        public async Task<bool> DeleteCollectionAsync(string tenantId, string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) return false;
            string? body = await SendAsync(HttpMethod.Delete, CollectionsUrl(tenantId) + "/" + id, null, true, token).ConfigureAwait(false);
            return body != null;
        }

        /// <inheritdoc />
        public async Task<bool> CollectionExistsAsync(string tenantId, string id, CancellationToken token = default)
        {
            RecallCollection? collection = await ReadCollectionAsync(tenantId, id, token).ConfigureAwait(false);
            return collection != null;
        }

        /// <inheritdoc />
        public async Task StoreChunksAsync(string tenantId, string collectionId, IReadOnlyList<ChunkDocument> chunks, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(collectionId)) throw new ArgumentNullException(nameof(collectionId));
            if (chunks == null || chunks.Count == 0) return;

            List<object> documents = new List<object>(chunks.Count);
            foreach (ChunkDocument chunk in chunks)
            {
                documents.Add(new
                {
                    DocumentKey = chunk.DocumentKey,
                    DocumentId = chunk.DocumentId,
                    Position = chunk.Position,
                    ContentType = "Text",
                    Content = chunk.Content,
                    Embeddings = chunk.Embedding,
                    Tags = chunk.Tags
                });
            }

            string json = JsonSerializer.Serialize(documents, _RequestJson);
            await PostJsonAsync(DocumentsUrl(tenantId, collectionId) + "/batch", json, token, isWrite: true).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<VectorSearchHit>> SearchAsync(string tenantId, string collectionId, IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            List<VectorSearchHit> hits = new List<VectorSearchHit>();
            if (String.IsNullOrWhiteSpace(collectionId) || embedding == null || embedding.Count == 0) return hits;

            object query = new
            {
                Vector = new
                {
                    SearchType = "CosineSimilarity",
                    Embeddings = embedding,
                    MinimumScore = minimumScore > 0 ? (double?)minimumScore : null
                },
                MaxResults = Math.Max(1, topK),
                TagFilter = BuildTagFilter(tags)
            };

            string responseBody = await PostJsonAsync(SearchUrl(tenantId, collectionId), JsonSerializer.Serialize(query, _RequestJson), token).ConfigureAwait(false);
            foreach (JsonElement docElement in EnumerateDocuments(responseBody))
            {
                string? nodeId = GetTag(docElement, "litegraphNodeId");
                if (String.IsNullOrEmpty(nodeId)) continue;
                hits.Add(new VectorSearchHit { NodeId = nodeId!, Score = GetDouble(docElement, "Score"), Content = GetString(docElement, "Content", "content") });
            }
            return hits;
        }

        /// <inheritdoc />
        public async Task<List<SearchHit>> SearchAsync(string tenantId, string collectionId, string query, int maxResults, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            List<SearchHit> hits = new List<SearchHit>();
            if (String.IsNullOrWhiteSpace(collectionId) || String.IsNullOrWhiteSpace(query)) return hits;

            object searchQuery = new
            {
                FullText = new
                {
                    Query = query,
                    SearchType = "TsRank",
                    Language = "english"
                },
                MaxResults = Math.Max(1, maxResults),
                TagFilter = BuildTagFilter(tags)
            };

            string responseBody = await PostJsonAsync(SearchUrl(tenantId, collectionId), JsonSerializer.Serialize(searchQuery, _RequestJson), token).ConfigureAwait(false);
            foreach (JsonElement docElement in EnumerateDocuments(responseBody))
            {
                SearchHit hit = new SearchHit
                {
                    DocumentId = GetString(docElement, "DocumentKey", "documentKey") ?? String.Empty,
                    Score = GetDouble(docElement, "Score", "TextScore"),
                    Snippet = GetString(docElement, "Content", "content"),
                    Tags = ParseTags(docElement)
                };
                hits.Add(hit);
            }
            return hits;
        }

        /// <inheritdoc />
        public async Task DeleteByTagAsync(string tenantId, string collectionId, string tagKey, string tagValue, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(collectionId) || String.IsNullOrWhiteSpace(tagKey)) return;

            object filter = new
            {
                TagFilter = new
                {
                    Required = new[] { new { Key = tagKey, Condition = "Equals", Value = tagValue } }
                }
            };
            await PostJsonAsync(DocumentsUrl(tenantId, collectionId) + "/delete/filter", JsonSerializer.Serialize(filter, _RequestJson), token, isWrite: true).ConfigureAwait(false);
        }

        /// <summary>
        /// Ensure the tenant Pneuma operates under exists in RecallDB. Idempotent: a 409 (already exists)
        /// is treated as success, so this is safe to call on every startup. RecallDB seeds only its own
        /// "default" tenant on first run, so the Pneuma tenant must be created here.
        /// </summary>
        /// <param name="tenantId">Tenant id to ensure (defaults to the configured tenant).</param>
        /// <param name="tenantName">Display name used when the tenant is created.</param>
        /// <param name="token">Cancellation token.</param>
        /// <exception cref="IntegrationClientException">Thrown when the create fails.</exception>
        public async Task EnsureTenantAsync(string tenantId, string tenantName, CancellationToken token = default)
        {
            string id = ResolveTenant(tenantId);
            if (String.IsNullOrWhiteSpace(id)) return;

            // RecallDB's create is INSERT-only (a duplicate id raises a DB error, not a clean 409), so make
            // this idempotent by checking existence first (GET returns 404 when the tenant does not exist).
            string readUrl = _BaseUrl + "/v1.0/tenants/" + id;
            string? existing = await SendAsync(HttpMethod.Get, readUrl, null, true, token).ConfigureAwait(false);
            if (existing != null) return;

            object body = new { Id = id, Name = String.IsNullOrWhiteSpace(tenantName) ? id : tenantName };
            string json = JsonSerializer.Serialize(body, _RequestJson);
            await SendAsync(HttpMethod.Put, _BaseUrl + "/v1.0/tenants", json, false, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private string ResolveTenant(string tenantId) => String.IsNullOrWhiteSpace(tenantId) ? _TenantId : tenantId;
        private string TenantUrl(string tenantId) => _BaseUrl + "/v1.0/tenants/" + ResolveTenant(tenantId);
        private string CollectionsUrl(string tenantId) => TenantUrl(tenantId) + "/collections";
        private string DocumentsUrl(string tenantId, string collectionId) => CollectionsUrl(tenantId) + "/" + collectionId + "/documents";
        private string SearchUrl(string tenantId, string collectionId) => CollectionsUrl(tenantId) + "/" + collectionId + "/search";

        private static object? BuildTagFilter(IReadOnlyDictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0) return null;
            List<object> required = new List<object>();
            foreach (KeyValuePair<string, string> tag in tags)
            {
                required.Add(new { Key = tag.Key, Condition = "Equals", Value = tag.Value });
            }
            return new { Required = required };
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

        private async Task<string> PostJsonAsync(string url, string json, CancellationToken token, bool isWrite = false)
        {
            return await SendResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(HttpMethod.Post, url, json),
                isWrite,
                token).ConfigureAwait(false);
        }

        private async Task<string> PostOrGetAsync(HttpMethod method, string url, string? json, CancellationToken token)
        {
            return await SendResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(method, url, json),
                isWrite: false,
                token).ConfigureAwait(false);
        }

        private async Task<string?> SendAsync(HttpMethod method, string url, string? json, bool nullOn404, CancellationToken token)
        {
            bool isWrite = method == HttpMethod.Put || method == HttpMethod.Delete;
            IntegrationResponse response = await SendRawResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(method, url, json),
                isWrite,
                token).ConfigureAwait(false);

            if (nullOn404 && response.StatusCode == (int)HttpStatusCode.NotFound) return null;
            if (!response.IsSuccess)
            {
                throw new IntegrationClientException("recalldb", RouteNormalizer.NormalizeUrl(url), response.StatusCode, Truncate(response.Body, 512));
            }
            return response.Body;
        }

        private IEnumerable<JsonElement> EnumerateDocuments(string responseBody)
        {
            List<JsonElement> results = new List<JsonElement>();
            if (String.IsNullOrWhiteSpace(responseBody)) return results;
            using (JsonDocument doc = JsonDocument.Parse(responseBody))
            {
                JsonElement array = FindArray(doc.RootElement, "Documents", "documents", "Data", "Objects");
                if (array.ValueKind != JsonValueKind.Array) return results;
                foreach (JsonElement item in array.EnumerateArray())
                {
                    results.Add(item.Clone());
                }
            }
            return results;
        }

        private static RecallCollection? MapCollection(JsonElement element)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            string? id = GetString(element, "Id", "id");
            if (String.IsNullOrEmpty(id)) return null;
            bool active = true;
            if (TryGetProperty(element, out JsonElement activeEl, "Active", "active") && activeEl.ValueKind == JsonValueKind.False) active = false;
            int dimensionality = 0;
            if (TryGetProperty(element, out JsonElement dimEl, "Dimensionality", "dimensionality") && dimEl.ValueKind == JsonValueKind.Number) dimEl.TryGetInt32(out dimensionality);
            return new RecallCollection
            {
                Id = id!,
                Name = GetString(element, "Name", "name") ?? String.Empty,
                Description = GetString(element, "Description", "description"),
                Dimensionality = dimensionality,
                Active = active
            };
        }

        private static Dictionary<string, string> ParseTags(JsonElement docElement)
        {
            Dictionary<string, string> tags = new Dictionary<string, string>(StringComparer.Ordinal);
            if (TryGetProperty(docElement, out JsonElement tagsEl, "Tags", "tags") && tagsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty prop in tagsEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String) tags[prop.Name] = prop.Value.GetString() ?? String.Empty;
                }
            }
            return tags;
        }

        private static string? GetTag(JsonElement docElement, string key)
        {
            if (TryGetProperty(docElement, out JsonElement tagsEl, "Tags", "tags") && tagsEl.ValueKind == JsonValueKind.Object)
            {
                if (tagsEl.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String) return value.GetString();
            }
            return null;
        }

        private static JsonElement Unwrap(JsonElement root)
        {
            if (root.ValueKind == JsonValueKind.Object && !TryGetProperty(root, out _, "Id", "id")
                && TryGetProperty(root, out JsonElement data, "Data", "data") && data.ValueKind == JsonValueKind.Object)
            {
                return data;
            }
            return root;
        }

        private static JsonElement FindArray(JsonElement root, params string[] names)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;
            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in names)
                {
                    if (TryGetProperty(root, out JsonElement named, name) && named.ValueKind == JsonValueKind.Array) return named;
                }
            }
            return default;
        }

        private static bool TryGetProperty(JsonElement element, out JsonElement value, params string[] names)
        {
            foreach (string name in names)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value)) return true;
            }
            value = default;
            return false;
        }

        private static string? GetString(JsonElement element, params string[] names)
        {
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.String) return value.GetString();
            return null;
        }

        private static double GetDouble(JsonElement element, params string[] names)
        {
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double result)) return result;
            return 0;
        }

        #endregion
    }
}
