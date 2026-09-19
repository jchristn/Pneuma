namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations.Interfaces;

    /// <summary>In-memory LiteGraph fake for graph merge, adjacency, and search tests.</summary>
    public class FakeLiteGraphClient : ILiteGraphClient
    {
        private readonly Dictionary<string, GraphNode> _Nodes = new Dictionary<string, GraphNode>();
        private readonly List<GraphEdge> _Edges = new List<GraphEdge>();
        private int _NodeCounter = 0;
        private int _EdgeCounter = 0;

        /// <summary>Number of nodes created.</summary>
        public int NodeCount { get { return _Nodes.Count; } }

        /// <summary>Number of edges created.</summary>
        public int EdgeCount { get { return _Edges.Count; } }

        /// <inheritdoc />
        public Task<string> EnsureGraphAsync(CancellationToken token = default)
        {
            return Task.FromResult("graph_test");
        }

        /// <inheritdoc />
        public Task<GraphNode> CreateNodeAsync(GraphNode node, CancellationToken token = default)
        {
            _NodeCounter++;
            node.Id = "node_" + _NodeCounter;
            _Nodes[node.Id] = node;
            return Task.FromResult(node);
        }

        /// <inheritdoc />
        public Task<GraphEdge> CreateEdgeAsync(GraphEdge edge, CancellationToken token = default)
        {
            _EdgeCounter++;
            edge.Id = "edge_" + _EdgeCounter;
            _Edges.Add(edge);
            return Task.FromResult(edge);
        }

        /// <inheritdoc />
        public Task<GraphEdge> UpdateEdgeAsync(GraphEdge edge, CancellationToken token = default)
        {
            for (int i = 0; i < _Edges.Count; i++)
            {
                if (_Edges[i].Id == edge.Id)
                {
                    _Edges[i] = edge;
                    break;
                }
            }
            return Task.FromResult(edge);
        }

        /// <inheritdoc />
        public Task<GraphNode?> ReadNodeAsync(string nodeId, CancellationToken token = default)
        {
            _Nodes.TryGetValue(nodeId, out GraphNode? node);
            return Task.FromResult(node);
        }

        /// <inheritdoc />
        public Task<GraphNode?> FindNodeByCanonicalAsync(string nodeType, string canonicalName, string subjectId, CancellationToken token = default)
        {
            foreach (GraphNode node in _Nodes.Values)
            {
                if (String.Equals(node.NodeType, nodeType, StringComparison.Ordinal) &&
                    String.Equals(node.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<GraphNode?>(node);
                }
            }
            return Task.FromResult<GraphNode?>(null);
        }

        /// <inheritdoc />
        public Task<List<GraphNode>> SearchNodesByTagsAsync(Dictionary<string, string> tags, int maxResults, CancellationToken token = default)
        {
            List<GraphNode> result = new List<GraphNode>();
            foreach (GraphNode node in _Nodes.Values)
            {
                if (result.Count >= maxResults) break;
                if (!MatchesTags(node, tags)) continue;
                result.Add(node);
            }
            return Task.FromResult(result);
        }

        private static bool MatchesTags(GraphNode node, Dictionary<string, string>? tags)
        {
            if (tags == null || tags.Count == 0) return true;
            foreach (KeyValuePair<string, string> tag in tags)
            {
                if (node.Tags == null || !node.Tags.TryGetValue(tag.Key, out string? value) || value != tag.Value) return false;
            }
            return true;
        }

        /// <inheritdoc />
        public Task<List<GraphNode>> GetNeighborsAsync(string nodeId, CancellationToken token = default)
        {
            List<GraphNode> result = new List<GraphNode>();
            foreach (GraphEdge edge in _Edges)
            {
                string? otherId = null;
                if (edge.FromNodeId == nodeId) otherId = edge.ToNodeId;
                else if (edge.ToNodeId == nodeId) otherId = edge.FromNodeId;
                if (otherId != null && _Nodes.TryGetValue(otherId, out GraphNode? other)) result.Add(other);
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<List<GraphEdge>> GetEdgesAsync(string nodeId, CancellationToken token = default)
        {
            List<GraphEdge> result = new List<GraphEdge>();
            foreach (GraphEdge edge in _Edges)
            {
                if (edge.FromNodeId == nodeId || edge.ToNodeId == nodeId) result.Add(edge);
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task<GraphSubgraph> GetSubgraphAsync(string nodeId, int maxDepth, int maxNodes, int maxEdges, CancellationToken token = default)
        {
            GraphSubgraph subgraph = new GraphSubgraph();
            if (!_Nodes.TryGetValue(nodeId, out GraphNode? origin)) return Task.FromResult(subgraph);

            HashSet<string> visitedNodes = new HashSet<string>(StringComparer.Ordinal) { nodeId };
            HashSet<string> visitedEdges = new HashSet<string>(StringComparer.Ordinal);
            subgraph.Nodes.Add(origin);
            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(nodeId, 0));

            while (queue.Count > 0)
            {
                KeyValuePair<string, int> current = queue.Dequeue();
                if (current.Value >= maxDepth) continue;
                foreach (GraphEdge edge in _Edges)
                {
                    string? other = edge.FromNodeId == current.Key ? edge.ToNodeId : (edge.ToNodeId == current.Key ? edge.FromNodeId : null);
                    if (other == null) continue;
                    if ((maxEdges <= 0 || subgraph.Edges.Count < maxEdges) && visitedEdges.Add(edge.Id)) subgraph.Edges.Add(edge);
                    if (visitedNodes.Contains(other)) continue;
                    if (maxNodes > 0 && subgraph.Nodes.Count >= maxNodes) continue;
                    if (!_Nodes.TryGetValue(other, out GraphNode? otherNode)) continue;
                    visitedNodes.Add(other);
                    subgraph.Nodes.Add(otherNode);
                    queue.Enqueue(new KeyValuePair<string, int>(other, current.Value + 1));
                }
            }
            return Task.FromResult(subgraph);
        }

        /// <inheritdoc />
        public Task<CommunityDetectionResult> DetectCommunitiesAsync(bool writeBack, int maxIterations, CancellationToken token = default)
        {
            // Fake community detection = weakly-connected components (a reasonable stand-in for Louvain on the
            // small, well-separated graphs used in tests).
            Dictionary<string, long> componentOf = new Dictionary<string, long>(StringComparer.Ordinal);
            long nextComponent = 0;
            foreach (string startId in _Nodes.Keys)
            {
                if (componentOf.ContainsKey(startId)) continue;
                long component = nextComponent++;
                Queue<string> queue = new Queue<string>();
                queue.Enqueue(startId);
                componentOf[startId] = component;
                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();
                    foreach (GraphEdge edge in _Edges)
                    {
                        string? other = edge.FromNodeId == id ? edge.ToNodeId : (edge.ToNodeId == id ? edge.FromNodeId : null);
                        if (other == null || componentOf.ContainsKey(other) || !_Nodes.ContainsKey(other)) continue;
                        componentOf[other] = component;
                        queue.Enqueue(other);
                    }
                }
            }

            CommunityDetectionResult result = new CommunityDetectionResult { CommunityCount = (int)nextComponent };
            foreach (KeyValuePair<string, long> entry in componentOf)
            {
                GraphNode node = _Nodes[entry.Key];
                result.Nodes.Add(new NodeCommunity { NodeId = entry.Key, Name = node.Name ?? string.Empty, Community = entry.Value });
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public Task DeleteNodeAsync(string nodeId, CancellationToken token = default)
        {
            _Nodes.Remove(nodeId);
            _Edges.RemoveAll(edge => edge.FromNodeId == nodeId || edge.ToNodeId == nodeId);
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteByJobAsync(string jobId, CancellationToken token = default)
        {
            List<string> removeNodeIds = new List<string>();
            foreach (KeyValuePair<string, GraphNode> entry in _Nodes)
            {
                if (entry.Value.Tags != null && entry.Value.Tags.TryGetValue(Ontology.TagAssertedByJob, out string? assertedBy) && assertedBy == jobId)
                {
                    removeNodeIds.Add(entry.Key);
                }
            }
            foreach (string id in removeNodeIds) _Nodes.Remove(id);

            _Edges.RemoveAll(edge =>
                (edge.Tags != null && edge.Tags.TryGetValue(Ontology.TagAssertedByJob, out string? assertedBy) && assertedBy == jobId)
                || removeNodeIds.Contains(edge.FromNodeId) || removeNodeIds.Contains(edge.ToNodeId));

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteBySubjectAsync(string subjectId, CancellationToken token = default)
        {
            List<string> removeNodeIds = new List<string>();
            foreach (KeyValuePair<string, GraphNode> entry in _Nodes)
            {
                if (entry.Value.Tags != null && entry.Value.Tags.TryGetValue(Ontology.TagSubjectId, out string? owner) && owner == subjectId)
                {
                    removeNodeIds.Add(entry.Key);
                }
            }
            foreach (string id in removeNodeIds) _Nodes.Remove(id);
            _Edges.RemoveAll(edge => removeNodeIds.Contains(edge.FromNodeId) || removeNodeIds.Contains(edge.ToNodeId));
            return Task.CompletedTask;
        }
    }
}
