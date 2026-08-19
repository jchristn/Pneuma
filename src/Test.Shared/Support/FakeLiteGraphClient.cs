namespace Test.Shared.Support
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
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
                result.Add(node);
            }
            return Task.FromResult(result);
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
