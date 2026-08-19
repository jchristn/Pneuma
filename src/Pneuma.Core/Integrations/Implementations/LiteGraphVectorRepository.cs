namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// <see cref="IVectorRepository"/> backed by LiteGraph v7's native vector store: embeddings are
    /// attached to graph nodes via <c>PUT /v1.0/tenants/{t}/vectors</c> and searched by cosine
    /// similarity via <c>POST /v1.0/tenants/{t}/vectors</c> (validated against <c>jchristn77/litegraph:v7.0.0</c>).
    /// The graph and its embeddings therefore live in one store; the exact-match tag filter is applied
    /// by LiteGraph against the target node's tags.
    /// </summary>
    public class LiteGraphVectorRepository : IntegrationClientBase, IVectorRepository
    {
        #region Private-Members

        // LiteGraph expects PascalCase request bodies; serialize without the global camelCase policy.
        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly string _BaseUrl;
        private readonly string? _BearerToken;
        private readonly string _TenantGuid;
        private readonly IGraphRepository _Graph;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize the LiteGraph-backed vector repository.</summary>
        /// <param name="baseUrl">Base URL of the LiteGraph service.</param>
        /// <param name="bearerToken">Optional bearer token; applied only when non-empty.</param>
        /// <param name="tenantGuid">LiteGraph tenant GUID embedded in vector paths.</param>
        /// <param name="graph">Graph repository used to resolve the target graph GUID.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required dependency is null.</exception>
        public LiteGraphVectorRepository(
            string baseUrl,
            string? bearerToken,
            string tenantGuid,
            IGraphRepository graph,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("litegraph", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken;
            _TenantGuid = tenantGuid ?? String.Empty;
            _Graph = graph;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task UpsertVectorAsync(string nodeId, IReadOnlyList<float> embedding, IReadOnlyDictionary<string, string>? tags = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(nodeId)) throw new ArgumentNullException(nameof(nodeId));
            if (embedding == null) throw new ArgumentNullException(nameof(embedding));

            string graph = await _Graph.EnsureGraphAsync(token).ConfigureAwait(false);
            List<float> vectors = new List<float>(embedding);

            object body = new
            {
                TenantGUID = _TenantGuid,
                GraphGUID = graph,
                NodeGUID = nodeId,
                Model = "pneuma",
                Dimensionality = vectors.Count,
                Content = String.Empty,
                Vectors = vectors
            };

            string url = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/vectors";
            await SendResilientAsync(
                "PUT /vectors",
                () => BuildRequest(HttpMethod.Put, url, JsonSerializer.Serialize(body, _RequestJson)),
                isWrite: true,
                token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<List<VectorSearchHit>> SearchAsync(IReadOnlyList<float> embedding, int topK, double minimumScore, IReadOnlyDictionary<string, string>? tags, CancellationToken token = default)
        {
            if (embedding == null) throw new ArgumentNullException(nameof(embedding));

            string graph = await _Graph.EnsureGraphAsync(token).ConfigureAwait(false);
            int limit = Math.Max(1, topK);

            Dictionary<string, string> tagFilter = new Dictionary<string, string>();
            if (tags != null)
            {
                foreach (KeyValuePair<string, string> tag in tags) tagFilter[tag.Key] = tag.Value;
            }

            object body = new
            {
                TenantGUID = _TenantGuid,
                GraphGUID = graph,
                Domain = "Node",
                SearchType = "CosineSimilarity",
                TopK = limit,
                MinimumScore = (float)minimumScore,
                Tags = tagFilter,
                Embeddings = new List<float>(embedding)
            };

            string url = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/vectors";
            string responseBody = await SendResilientAsync(
                "POST /vectors",
                () => BuildRequest(HttpMethod.Post, url, JsonSerializer.Serialize(body, _RequestJson)),
                isWrite: false,
                token).ConfigureAwait(false);

            return ParseHits(responseBody, limit);
        }

        #endregion

        #region Private-Methods

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

        private static List<VectorSearchHit> ParseHits(string body, int limit)
        {
            List<VectorSearchHit> hits = new List<VectorSearchHit>();
            if (String.IsNullOrWhiteSpace(body)) return hits;

            using (JsonDocument doc = JsonDocument.Parse(body))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return hits;

                foreach (JsonElement item in doc.RootElement.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;

                    double score = 0.0;
                    if (item.TryGetProperty("Score", out JsonElement scoreEl) && scoreEl.ValueKind == JsonValueKind.Number && scoreEl.TryGetDouble(out double s)) score = s;

                    string nodeId = String.Empty;
                    GraphNode? node = null;
                    if (item.TryGetProperty("Node", out JsonElement nodeEl) && nodeEl.ValueKind == JsonValueKind.Object)
                    {
                        node = MapNode(nodeEl);
                        nodeId = node.Id;
                    }

                    if (String.IsNullOrEmpty(nodeId)) continue;
                    hits.Add(new VectorSearchHit { NodeId = nodeId, Score = score, Node = node });
                    if (hits.Count >= limit) break;
                }
            }

            return hits;
        }

        private static GraphNode MapNode(JsonElement element)
        {
            GraphNode node = new GraphNode
            {
                Id = GetString(element, "GUID") ?? String.Empty,
                Name = GetString(element, "Name") ?? String.Empty
            };

            if (element.TryGetProperty("Labels", out JsonElement labels) && labels.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement label in labels.EnumerateArray())
                {
                    if (label.ValueKind == JsonValueKind.String)
                    {
                        string? value = label.GetString();
                        if (value != null) node.Labels.Add(value);
                    }
                }
            }

            if (element.TryGetProperty("Tags", out JsonElement tags) && tags.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty tag in tags.EnumerateObject())
                {
                    if (tag.Value.ValueKind == JsonValueKind.String) node.Tags[tag.Name] = tag.Value.GetString() ?? String.Empty;
                }
            }

            if (element.TryGetProperty("Data", out JsonElement data) && data.ValueKind == JsonValueKind.Object)
            {
                node.NodeType = GetString(data, "nodeType") ?? GetString(data, "NodeType") ?? (node.Labels.Count > 0 ? node.Labels[0] : String.Empty);
                node.Content = GetString(data, "content") ?? GetString(data, "Content");
            }
            else if (node.Labels.Count > 0)
            {
                node.NodeType = node.Labels[0];
            }

            return node;
        }

        private static string? GetString(JsonElement element, string name)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
            return null;
        }

        #endregion
    }
}
