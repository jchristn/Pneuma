namespace Pneuma.Core.Integrations.Implementations
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Abstractions;
    using Pneuma.Core.Integrations.Interfaces;
    using Pneuma.Core.Observability;
    using Pneuma.Core.Serialization;

    /// <summary>
    /// HTTP client for the LiteGraph knowledge-graph store. Paths embed the tenant and graph GUIDs;
    /// node and edge JSON fields are PascalCase. Read/search operations used for entity resolution
    /// are best-effort and swallow errors.
    /// </summary>
    public class LiteGraphClient : IntegrationClientBase, ILiteGraphClient, IServiceProbe
    {
        #region Private-Members

        // LiteGraph deserializes request bodies case-sensitively and expects PascalCase (Name, Labels,
        // Tags, Data, From, To). Serialize WITHOUT the global camelCase policy, otherwise LiteGraph
        // ignores the fields and stores blank graphs/nodes/edges.
        private static readonly JsonSerializerOptions _RequestJson = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        // Bounded concurrency for bulk node/edge deletion during cascade cleanup.
        private const int _MaxDeleteConcurrency = 8;

        private readonly string _BaseUrl;
        private readonly string? _BearerToken;
        private readonly string _TenantGuid;
        private readonly SemaphoreSlim _GraphLock = new SemaphoreSlim(1, 1);

        private string _GraphGuid;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize a new LiteGraph client.</summary>
        /// <param name="baseUrl">Base URL of the LiteGraph service.</param>
        /// <param name="bearerToken">Optional bearer token; authentication is applied only when non-empty.</param>
        /// <param name="tenantGuid">Tenant GUID embedded in all paths.</param>
        /// <param name="graphGuid">Optional graph GUID; resolved via <see cref="EnsureGraphAsync"/> when null/empty.</param>
        /// <param name="timeoutMilliseconds">Per-attempt timeout in ms.</param>
        /// <param name="maxConcurrentRequests">Max concurrent requests to this service.</param>
        /// <param name="retryCount">Retry count for transient failures on reads.</param>
        /// <param name="retryDelayMilliseconds">Delay between retries in ms.</param>
        /// <param name="handler">Optional message handler for tests.</param>
        public LiteGraphClient(
            string baseUrl,
            string? bearerToken,
            string tenantGuid,
            string? graphGuid,
            int timeoutMilliseconds = 100000,
            int maxConcurrentRequests = 8,
            int retryCount = 2,
            int retryDelayMilliseconds = 500,
            HttpMessageHandler? handler = null)
            : base("litegraph", timeoutMilliseconds, maxConcurrentRequests, retryCount, retryDelayMilliseconds, handler)
        {
            if (String.IsNullOrWhiteSpace(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            _BaseUrl = baseUrl.TrimEnd('/');
            _BearerToken = bearerToken;
            // tenantGuid may be empty when LiteGraph is not yet configured; graph operations will
            // surface the misconfiguration at call time rather than preventing server startup.
            _TenantGuid = tenantGuid ?? String.Empty;
            _GraphGuid = String.IsNullOrWhiteSpace(graphGuid) ? String.Empty : graphGuid!;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<IntegrationHealthResult> ProbeAsync(CancellationToken token = default)
        {
            return ProbeResilientAsync("probe", () => new HttpRequestMessage(HttpMethod.Get, _BaseUrl + "/"), token);
        }

        /// <inheritdoc />
        public async Task<string> EnsureGraphAsync(CancellationToken token = default)
        {
            if (!String.IsNullOrEmpty(_GraphGuid)) return _GraphGuid;

            await _GraphLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (!String.IsNullOrEmpty(_GraphGuid)) return _GraphGuid;

                // Reuse an existing "Pneuma" graph if one is already present, so the server does not create a
                // fresh (empty) graph on every restart.
                string listBody = await SendAsync(HttpMethod.Get, _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs", null, token).ConfigureAwait(false);
                using (JsonDocument listDoc = JsonDocument.Parse(String.IsNullOrWhiteSpace(listBody) ? "[]" : listBody))
                {
                    JsonElement array = FindArrayOfObjects(listDoc.RootElement, new[] { "Objects", "Graphs", "Data", "Results" });
                    if (array.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement item in array.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.Object) continue;
                            string? name = GetStringProperty(item, "Name", "name");
                            if (name != null && String.Equals(name, "Pneuma", StringComparison.Ordinal))
                            {
                                string? existing = GetStringProperty(item, "GUID", "Guid", "guid");
                                if (!String.IsNullOrEmpty(existing)) { _GraphGuid = existing!; return _GraphGuid; }
                            }
                        }
                    }
                }

                string body = await SendAsync(HttpMethod.Put, _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs",
                    JsonSerializer.Serialize(new { Name = "Pneuma" }, _RequestJson), token, isWrite: true).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    string? guid = GetStringProperty(doc.RootElement, "GUID", "Guid", "guid");
                    if (String.IsNullOrEmpty(guid))
                    {
                        throw new HttpRequestException("LiteGraph graph creation did not return a GUID.");
                    }
                    _GraphGuid = guid!;
                    return _GraphGuid;
                }
            }
            finally
            {
                _GraphLock.Release();
            }
        }

        /// <inheritdoc />
        public async Task<GraphNode> CreateNodeAsync(GraphNode node, CancellationToken token = default)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            string graph = await EnsureGraphAsync(token).ConfigureAwait(false);

            object requestBody = new
            {
                Name = node.Name,
                Labels = node.Labels,
                Tags = node.Tags,
                Data = new { content = node.Content, nodeType = node.NodeType }
            };

            string body = await SendAsync(HttpMethod.Put,
                _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes",
                JsonSerializer.Serialize(requestBody, _RequestJson), token, isWrite: true).ConfigureAwait(false);

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
            {
                string? guid = GetStringProperty(doc.RootElement, "GUID", "Guid", "guid");
                if (!String.IsNullOrEmpty(guid)) node.Id = guid!;
            }

            return node;
        }

        /// <inheritdoc />
        public async Task<GraphEdge> CreateEdgeAsync(GraphEdge edge, CancellationToken token = default)
        {
            if (edge == null) throw new ArgumentNullException(nameof(edge));
            string graph = await EnsureGraphAsync(token).ConfigureAwait(false);

            object requestBody = new
            {
                From = edge.FromNodeId,
                To = edge.ToNodeId,
                Name = edge.EdgeType,
                Cost = 1,
                Labels = new List<string> { edge.EdgeType },
                Tags = edge.Tags
            };

            string body = await SendAsync(HttpMethod.Put,
                _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/edges",
                JsonSerializer.Serialize(requestBody, _RequestJson), token, isWrite: true).ConfigureAwait(false);

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
            {
                string? guid = GetStringProperty(doc.RootElement, "GUID", "Guid", "guid");
                if (!String.IsNullOrEmpty(guid)) edge.Id = guid!;
            }

            return edge;
        }

        /// <inheritdoc />
        public async Task<GraphNode?> ReadNodeAsync(string nodeId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(nodeId)) throw new ArgumentNullException(nameof(nodeId));
            string graph = await EnsureGraphAsync(token).ConfigureAwait(false);

            string url = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes/" + nodeId;

            IntegrationResponse response = await SendRawResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(HttpMethod.Get, url, null),
                isWrite: false,
                token).ConfigureAwait(false);

            if (response.StatusCode == (int)HttpStatusCode.NotFound) return null;
            if (!response.IsSuccess)
            {
                throw new IntegrationClientException("litegraph", RouteNormalizer.NormalizeUrl(url), response.StatusCode, Truncate(response.Body, 512));
            }

            using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(response.Body) ? "{}" : response.Body))
            {
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
                return MapNode(doc.RootElement);
            }
        }

        /// <inheritdoc />
        public async Task<GraphNode?> FindNodeByCanonicalAsync(string nodeType, string canonicalName, string subjectId, CancellationToken token = default)
        {
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                object requestBody = new
                {
                    Tags = new Dictionary<string, string>
                    {
                        { "nodeType", nodeType },
                        { "canonicalName", canonicalName },
                        { "subjectId", subjectId }
                    }
                };

                string body = await SendAsync(HttpMethod.Post,
                    _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes/search",
                    JsonSerializer.Serialize(requestBody, _RequestJson), token).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement array = FindNodeArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return null;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object) return MapNode(item);
                    }
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <inheritdoc />
        public async Task<List<GraphNode>> SearchNodesByTagsAsync(Dictionary<string, string> tags, int maxResults, CancellationToken token = default)
        {
            List<GraphNode> nodes = new List<GraphNode>();
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                string body = await SendAsync(HttpMethod.Post,
                    _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes/search",
                    JsonSerializer.Serialize(new { Tags = tags ?? new Dictionary<string, string>() }, _RequestJson), token).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement array = FindNodeArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return nodes;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        nodes.Add(MapNode(item));
                        if (nodes.Count >= maxResults) break;
                    }
                }
            }
            catch
            {
                return new List<GraphNode>();
            }

            return nodes;
        }

        /// <inheritdoc />
        public async Task<List<GraphNode>> GetNeighborsAsync(string nodeId, CancellationToken token = default)
        {
            List<GraphNode> nodes = new List<GraphNode>();
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                string body = await SendAsync(HttpMethod.Get,
                    _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes/" + nodeId + "/neighbors",
                    null, token).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement array = FindNodeArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return nodes;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object) nodes.Add(MapNode(item));
                    }
                }
            }
            catch
            {
                return new List<GraphNode>();
            }

            return nodes;
        }

        /// <inheritdoc />
        public async Task<List<GraphEdge>> GetEdgesAsync(string nodeId, CancellationToken token = default)
        {
            List<GraphEdge> edges = new List<GraphEdge>();
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                string body = await SendAsync(HttpMethod.Get,
                    _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/nodes/" + nodeId + "/edges",
                    null, token).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement array = FindEdgeArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return edges;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object) edges.Add(MapEdge(item));
                    }
                }
            }
            catch
            {
                return new List<GraphEdge>();
            }

            return edges;
        }

        /// <inheritdoc />
        public async Task DeleteByJobAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));

            Dictionary<string, string> jobTag = new Dictionary<string, string> { { Ontology.TagAssertedByJob, jobId } };

            // Delete nodes this job asserted (source + newly-created entity nodes). Deleting a node
            // cascades its attached edges; nodes reused from earlier jobs are not tagged here and survive.
            List<GraphNode> nodes = await SearchNodesByTagsAsync(jobTag, 10000, token).ConfigureAwait(false);
            await DeleteResourcesAsync("nodes", NodeIds(nodes), token).ConfigureAwait(false);

            // Delete any remaining edges asserted by this job (e.g. relationships between two reused
            // shared nodes, which the node deletions above would not have removed).
            List<GraphEdge> edges = await SearchEdgesByTagsAsync(jobTag, 10000, token).ConfigureAwait(false);
            await DeleteResourcesAsync("edges", EdgeIds(edges), token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task DeleteBySubjectAsync(string subjectId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(subjectId)) throw new ArgumentNullException(nameof(subjectId));

            Dictionary<string, string> subjectTag = new Dictionary<string, string> { { Ontology.TagSubjectId, subjectId } };

            // All of a subject's nodes (source nodes and per-subject entity nodes) carry the subjectId tag.
            // Deleting each node cascades its attached edges, removing the subject's entire subgraph.
            List<GraphNode> nodes = await SearchNodesByTagsAsync(subjectTag, 100000, token).ConfigureAwait(false);
            await DeleteResourcesAsync("nodes", NodeIds(nodes), token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<List<GraphEdge>> SearchEdgesByTagsAsync(Dictionary<string, string> tags, int maxResults, CancellationToken token)
        {
            List<GraphEdge> edges = new List<GraphEdge>();
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                string body = await SendAsync(HttpMethod.Post,
                    _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/edges/search",
                    JsonSerializer.Serialize(new { Tags = tags ?? new Dictionary<string, string>() }, _RequestJson), token).ConfigureAwait(false);

                using (JsonDocument doc = JsonDocument.Parse(String.IsNullOrWhiteSpace(body) ? "{}" : body))
                {
                    JsonElement array = FindEdgeArray(doc.RootElement);
                    if (array.ValueKind != JsonValueKind.Array) return edges;
                    foreach (JsonElement item in array.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        edges.Add(MapEdge(item));
                        if (edges.Count >= maxResults) break;
                    }
                }
            }
            catch
            {
                return new List<GraphEdge>();
            }

            return edges;
        }

        private async Task DeleteResourceAsync(string resource, string id, CancellationToken token)
        {
            // Best-effort: a resource already removed (e.g. an edge cascaded by a node delete) must not
            // abort the wider cascade, so 404s and other failures are swallowed.
            try
            {
                string graph = await EnsureGraphAsync(token).ConfigureAwait(false);
                string url = _BaseUrl + "/v1.0/tenants/" + _TenantGuid + "/graphs/" + graph + "/" + resource + "/" + id;

                await SendRawResilientAsync(
                    "DELETE " + resource,
                    () => BuildRequest(HttpMethod.Delete, url, null),
                    isWrite: true,
                    token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort: a resource already removed (e.g. an edge cascaded by a node delete)
                // must not abort the wider cascade. The base already recorded the failure metric.
            }
        }

        private async Task DeleteResourcesAsync(string resource, List<string> ids, CancellationToken token)
        {
            if (ids.Count == 0) return;

            // Delete concurrently rather than one-at-a-time: a subject/job can have many nodes and edges, and
            // sequential round-trips make cascade deletion very slow. Each delete is already best-effort, and
            // the client's per-service concurrency bulkhead caps the real parallelism against LiteGraph.
            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = _MaxDeleteConcurrency, CancellationToken = token };
            await Parallel.ForEachAsync(ids, options, async (id, ct) =>
                await DeleteResourceAsync(resource, id, ct).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private static List<string> NodeIds(List<GraphNode> nodes)
        {
            List<string> ids = new List<string>();
            foreach (GraphNode node in nodes)
            {
                if (!String.IsNullOrEmpty(node.Id)) ids.Add(node.Id);
            }
            return ids;
        }

        private static List<string> EdgeIds(List<GraphEdge> edges)
        {
            List<string> ids = new List<string>();
            foreach (GraphEdge edge in edges)
            {
                if (!String.IsNullOrEmpty(edge.Id)) ids.Add(edge.Id);
            }
            return ids;
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string url, string? json)
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
        }

        private async Task<string> SendAsync(HttpMethod method, string url, string? json, CancellationToken token, bool isWrite = false)
        {
            return await SendResilientAsync(
                RouteNormalizer.NormalizeUrl(url),
                () => BuildRequest(method, url, json),
                isWrite,
                token).ConfigureAwait(false);
        }

        private static GraphNode MapNode(JsonElement element)
        {
            GraphNode node = new GraphNode
            {
                Id = GetStringProperty(element, "GUID", "Guid", "guid") ?? String.Empty,
                Name = GetStringProperty(element, "Name", "name") ?? String.Empty
            };

            List<string> labels = GetStringList(element, "Labels", "labels");
            node.Labels = labels;

            Dictionary<string, string> tags = GetStringMap(element, "Tags", "tags");
            node.Tags = tags;

            string? nodeType = null;
            string? content = null;
            if (TryGetProperty(element, out JsonElement data, "Data", "data") && data.ValueKind == JsonValueKind.Object)
            {
                nodeType = GetStringProperty(data, "nodeType", "NodeType");
                content = GetStringProperty(data, "content", "Content");
            }

            if (String.IsNullOrEmpty(nodeType) && labels.Count > 0) nodeType = labels[0];
            node.NodeType = nodeType ?? String.Empty;
            node.Content = content;

            return node;
        }

        private static GraphEdge MapEdge(JsonElement element)
        {
            GraphEdge edge = new GraphEdge
            {
                Id = GetStringProperty(element, "GUID", "Guid", "guid") ?? String.Empty,
                FromNodeId = GetStringProperty(element, "From", "from") ?? String.Empty,
                ToNodeId = GetStringProperty(element, "To", "to") ?? String.Empty,
                Tags = GetStringMap(element, "Tags", "tags")
            };

            string? edgeType = GetStringProperty(element, "Name", "name");
            if (String.IsNullOrEmpty(edgeType))
            {
                List<string> labels = GetStringList(element, "Labels", "labels");
                if (labels.Count > 0) edgeType = labels[0];
            }
            edge.EdgeType = edgeType ?? String.Empty;

            return edge;
        }

        private static JsonElement FindNodeArray(JsonElement root)
        {
            return FindArrayOfObjects(root, new[] { "Objects", "Nodes", "Data", "Results" });
        }

        private static JsonElement FindEdgeArray(JsonElement root)
        {
            return FindArrayOfObjects(root, new[] { "Objects", "Edges", "Data", "Results" });
        }

        private static JsonElement FindArrayOfObjects(JsonElement root, string[] preferredNames)
        {
            if (root.ValueKind == JsonValueKind.Array) return root;

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (string name in preferredNames)
                {
                    if (TryGetProperty(root, out JsonElement named, name) && named.ValueKind == JsonValueKind.Array)
                    {
                        return named;
                    }
                }
                foreach (JsonProperty prop in root.EnumerateObject())
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

        private static List<string> GetStringList(JsonElement element, params string[] names)
        {
            List<string> result = new List<string>();
            if (TryGetProperty(element, out JsonElement value, names) && value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string? s = item.GetString();
                        if (s != null) result.Add(s);
                    }
                }
            }
            return result;
        }

        private static Dictionary<string, string> GetStringMap(JsonElement element, params string[] names)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();
            if (TryGetProperty(element, out JsonElement map, names) && map.ValueKind == JsonValueKind.Object)
            {
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
            }
            return result;
        }

        #endregion
    }
}
