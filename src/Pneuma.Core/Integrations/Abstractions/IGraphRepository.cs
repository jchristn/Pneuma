namespace Pneuma.Core.Integrations.Abstractions
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// Provider-neutral knowledge-graph store: node/edge creation, node reads and neighbor traversal,
    /// tag-scoped search, and cascade deletion by provenance tag. Backed today by LiteGraph, but the
    /// contract is deliberately vendor-neutral so the graph backend can be swapped without touching
    /// ingestion or retrieval orchestration.
    /// </summary>
    public interface IGraphRepository
    {
        /// <summary>Ensure the backing graph exists and return its identifier.</summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Graph identifier.</returns>
        Task<string> EnsureGraphAsync(CancellationToken token = default);

        /// <summary>Create a node and return it with its assigned identifier.</summary>
        /// <param name="node">Node to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created node.</returns>
        Task<GraphNode> CreateNodeAsync(GraphNode node, CancellationToken token = default);

        /// <summary>Create an edge and return it with its assigned identifier.</summary>
        /// <param name="edge">Edge to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created edge.</returns>
        Task<GraphEdge> CreateEdgeAsync(GraphEdge edge, CancellationToken token = default);

        /// <summary>
        /// Update an existing edge's tags (identified by <see cref="GraphEdge.Id"/>), used to consolidate a
        /// relationship's accumulated weight and corroboration count when it is asserted again rather than
        /// creating a duplicate edge.
        /// </summary>
        /// <param name="edge">The edge to update; its <see cref="GraphEdge.Id"/> must be set.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The updated edge.</returns>
        Task<GraphEdge> UpdateEdgeAsync(GraphEdge edge, CancellationToken token = default);

        /// <summary>Read a node by identifier.</summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The node, or null if not found.</returns>
        Task<GraphNode?> ReadNodeAsync(string nodeId, CancellationToken token = default);

        /// <summary>
        /// Find an existing node of a given type with a matching canonical name within a subject,
        /// used for entity resolution during merge.
        /// </summary>
        /// <param name="nodeType">Node type.</param>
        /// <param name="canonicalName">Canonical name.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The matching node, or null.</returns>
        Task<GraphNode?> FindNodeByCanonicalAsync(string nodeType, string canonicalName, string subjectId, CancellationToken token = default);

        /// <summary>Search nodes by an exact-match tag set.</summary>
        /// <param name="tags">Tags to match.</param>
        /// <param name="maxResults">Maximum results.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching nodes.</returns>
        Task<List<GraphNode>> SearchNodesByTagsAsync(Dictionary<string, string> tags, int maxResults, CancellationToken token = default);

        /// <summary>Get the neighbor nodes of a node.</summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Neighbor nodes.</returns>
        Task<List<GraphNode>> GetNeighborsAsync(string nodeId, CancellationToken token = default);

        /// <summary>Get all edges attached to a node.</summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Edges.</returns>
        Task<List<GraphEdge>> GetEdgesAsync(string nodeId, CancellationToken token = default);

        /// <summary>
        /// Extract a bounded, depth-limited subgraph reachable from a node (server-side breadth-first
        /// traversal), returning the nodes reached within the hop depth and the edges among them. Enables
        /// multi-hop retrieval expansion without client-side breadth-first fan-out.
        /// </summary>
        /// <param name="nodeId">The node to expand from.</param>
        /// <param name="maxDepth">Maximum hop depth from the origin node.</param>
        /// <param name="maxNodes">Maximum nodes to return (0 = unbounded).</param>
        /// <param name="maxEdges">Maximum edges to return (0 = unbounded).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The extracted subgraph.</returns>
        Task<GraphSubgraph> GetSubgraphAsync(string nodeId, int maxDepth, int maxNodes, int maxEdges, CancellationToken token = default);

        /// <summary>
        /// Run community detection (Louvain) over the graph, returning per-node community assignments. When
        /// <paramref name="writeBack"/> is true, the community id is also persisted onto each node so it is
        /// queryable; otherwise the assignment is only returned.
        /// </summary>
        /// <param name="writeBack">Whether to persist the community id onto each node.</param>
        /// <param name="maxIterations">Maximum iterations for the algorithm.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The community assignments.</returns>
        Task<CommunityDetectionResult> DetectCommunitiesAsync(bool writeBack, int maxIterations, CancellationToken token = default);

        /// <summary>Delete a single node (and its attached edges) by identifier. Best-effort; a missing node is not an error.</summary>
        /// <param name="nodeId">Node identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteNodeAsync(string nodeId, CancellationToken token = default);

        /// <summary>
        /// Delete every node and edge asserted by a given ingestion job (tagged with its id), used to
        /// cascade-remove a link's graph contributions. Nodes reused from earlier jobs (shared entity
        /// nodes not tagged by this job) are preserved. Best-effort — per-element failures are swallowed.
        /// </summary>
        /// <param name="jobId">Ingestion job identifier that asserted the graph elements.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByJobAsync(string jobId, CancellationToken token = default);

        /// <summary>
        /// Delete every node (and its attached edges) belonging to a subject — matched on the subjectId
        /// tag — used to cascade-remove a subject's entire subgraph. Entity nodes are resolved per subject,
        /// so this does not affect other subjects. Best-effort — per-element failures are swallowed.
        /// </summary>
        /// <param name="subjectId">Subject identifier whose graph nodes should be removed.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string subjectId, CancellationToken token = default);
    }
}
