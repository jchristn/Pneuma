namespace Pneuma.Core.Graph
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Merges a model-produced candidate subgraph into the live LiteGraph, performing best-effort
    /// entity resolution by (node type, canonical name) within a subject, attaching provenance to a
    /// Source node, and tagging rights, authority, and confidence.
    /// </summary>
    public class SubgraphMerger
    {
        #region Private-Members

        private readonly IGraphRepository _Graph;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the merger.</summary>
        /// <param name="graph">LiteGraph client.</param>
        public SubgraphMerger(IGraphRepository graph)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            _Graph = graph;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Canonicalize the candidate subgraph's node and edge types in place — coercing each to a built-in
        /// ontology type when recognized, otherwise normalizing casing/spelling — so variance does not fragment
        /// the ontology into near-duplicate labels. Pure (no graph I/O); run as its own pipeline step before the
        /// node merge.
        /// </summary>
        /// <param name="subgraph">Candidate subgraph to normalize in place.</param>
        /// <returns>The number of node and edge types normalized.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> is null.</exception>
        public static int Canonicalize(CandidateSubgraph subgraph)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));

            int normalized = 0;
            foreach (CandidateNode candidate in subgraph.Nodes)
            {
                if (String.IsNullOrWhiteSpace(candidate.NodeType) || String.IsNullOrWhiteSpace(candidate.Name)) continue;
                candidate.NodeType = Ontology.CanonicalNodeType(candidate.NodeType);
                normalized++;
            }
            foreach (CandidateEdge candidate in subgraph.Edges)
            {
                if (String.IsNullOrWhiteSpace(candidate.EdgeType)) continue;
                candidate.EdgeType = Ontology.CanonicalEdgeType(candidate.EdgeType);
                normalized++;
            }
            return normalized;
        }

        /// <summary>
        /// Merge the candidate subgraph's nodes into the live graph, resolving each by (canonical type, canonical
        /// name) within the subject and attaching provenance. Node types are assumed already canonicalized (see
        /// <see cref="Canonicalize"/>).
        /// </summary>
        /// <param name="subgraph">Candidate subgraph.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="subjectId">Owning subject identifier.</param>
        /// <param name="sourceNodeId">Provenance Source node identifier, if one was created.</param>
        /// <param name="jobId">Ingestion job identifier that asserted the elements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created/resolved node identifiers and the ref→id map for relationship consolidation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> is null.</exception>
        public async Task<NodeMergeResult> MergeNodesAsync(
            CandidateSubgraph subgraph,
            string tenantId,
            string subjectId,
            string? sourceNodeId,
            string jobId,
            CancellationToken token = default)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));

            NodeMergeResult result = new NodeMergeResult();

            foreach (CandidateNode candidate in subgraph.Nodes)
            {
                if (String.IsNullOrWhiteSpace(candidate.NodeType) || String.IsNullOrWhiteSpace(candidate.Name)) continue;

                string canonical = String.IsNullOrWhiteSpace(candidate.CanonicalName)
                    ? candidate.Name
                    : candidate.CanonicalName!;

                GraphNode? existing = await _Graph
                    .FindNodeByCanonicalAsync(candidate.NodeType, canonical, subjectId, token)
                    .ConfigureAwait(false);

                string nodeId;
                if (existing != null && !String.IsNullOrEmpty(existing.Id))
                {
                    nodeId = existing.Id;
                }
                else
                {
                    GraphNode node = BuildNode(candidate, canonical, tenantId, subjectId, jobId, sourceNodeId);
                    GraphNode created = await _Graph.CreateNodeAsync(node, token).ConfigureAwait(false);
                    nodeId = created.Id;

                    if (!String.IsNullOrEmpty(sourceNodeId))
                    {
                        await CreateProvenanceEdgeAsync(nodeId, sourceNodeId!, jobId, token).ConfigureAwait(false);
                    }
                }

                if (!String.IsNullOrEmpty(candidate.Ref) && !result.RefToId.ContainsKey(candidate.Ref))
                {
                    result.RefToId[candidate.Ref] = nodeId;
                }
                if (!result.NodeIds.Contains(nodeId)) result.NodeIds.Add(nodeId);
            }

            return result;
        }

        /// <summary>
        /// Consolidate the candidate subgraph's relationships into the live graph: a re-asserted edge (same
        /// from/to/type) accumulates weight (noisy-OR) and corroboration count in place rather than duplicating,
        /// otherwise a new edge is created. Edge types are assumed already canonicalized (see <see cref="Canonicalize"/>).
        /// </summary>
        /// <param name="subgraph">Candidate subgraph.</param>
        /// <param name="refToId">Map from candidate node reference to resolved graph node id (from the node merge).</param>
        /// <param name="sourceNodeId">Provenance Source node identifier, if one was created.</param>
        /// <param name="jobId">Ingestion job identifier that asserted the elements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created/updated edge identifiers plus created and consolidated counts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> or <paramref name="refToId"/> is null.</exception>
        public async Task<EdgeConsolidationResult> ConsolidateEdgesAsync(
            CandidateSubgraph subgraph,
            Dictionary<string, string> refToId,
            string? sourceNodeId,
            string jobId,
            CancellationToken token = default)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));
            if (refToId == null) throw new ArgumentNullException(nameof(refToId));

            EdgeConsolidationResult result = new EdgeConsolidationResult();

            foreach (CandidateEdge candidate in subgraph.Edges)
            {
                if (String.IsNullOrWhiteSpace(candidate.EdgeType)) continue;
                if (!refToId.TryGetValue(candidate.FromRef, out string? fromId)) continue;
                if (!refToId.TryGetValue(candidate.ToRef, out string? toId)) continue;

                double confidence = Math.Clamp(candidate.Confidence, 0.0, 1.0);

                // Cross-source consolidation: if this relationship (same from/to/type) already exists — from an
                // earlier source, or earlier in this same merge — accumulate its weight and corroboration count
                // in place rather than creating a duplicate edge. Weight uses a noisy-OR so more corroborating
                // sources push it toward 1.0 without ever exceeding it.
                GraphEdge? existing = await FindExistingEdgeAsync(fromId, toId, candidate.EdgeType, token).ConfigureAwait(false);
                if (existing != null && !String.IsNullOrEmpty(existing.Id))
                {
                    double oldWeight = ReadTagDouble(existing, Ontology.TagWeight, ReadTagDouble(existing, Ontology.TagConfidence, 0.0));
                    int oldCount = ReadTagInt(existing, Ontology.TagCorroborationCount, 1);
                    double newWeight = oldWeight + ((1.0 - oldWeight) * confidence);
                    double bestConfidence = Math.Max(ReadTagDouble(existing, Ontology.TagConfidence, 0.0), confidence);

                    existing.Tags[Ontology.TagWeight] = newWeight.ToString("F4", CultureInfo.InvariantCulture);
                    existing.Tags[Ontology.TagCorroborationCount] = (oldCount + 1).ToString(CultureInfo.InvariantCulture);
                    existing.Tags[Ontology.TagConfidence] = bestConfidence.ToString("F3", CultureInfo.InvariantCulture);

                    await _Graph.UpdateEdgeAsync(existing, token).ConfigureAwait(false);
                    if (!result.EdgeIds.Contains(existing.Id)) result.EdgeIds.Add(existing.Id);
                    result.ConsolidatedCount++;
                    continue;
                }

                GraphEdge edge = new GraphEdge
                {
                    EdgeType = candidate.EdgeType,
                    FromNodeId = fromId,
                    ToNodeId = toId,
                    Tags = new Dictionary<string, string>
                    {
                        { Ontology.TagConfidence, confidence.ToString("F3", CultureInfo.InvariantCulture) },
                        { Ontology.TagWeight, confidence.ToString("F4", CultureInfo.InvariantCulture) },
                        { Ontology.TagCorroborationCount, "1" },
                        { Ontology.TagAssertedByJob, jobId }
                    }
                };
                if (!String.IsNullOrEmpty(sourceNodeId)) edge.Tags[Ontology.TagSourceId] = sourceNodeId!;

                GraphEdge created = await _Graph.CreateEdgeAsync(edge, token).ConfigureAwait(false);
                if (!String.IsNullOrEmpty(created.Id))
                {
                    result.EdgeIds.Add(created.Id);
                    result.CreatedCount++;
                }
            }

            return result;
        }

        /// <summary>
        /// Convenience wrapper that runs the full merge in one call — canonicalize, merge nodes, then consolidate
        /// relationships — returning the combined node and edge identifiers. The ingestion pipeline invokes the
        /// three steps separately (so each is measured independently); this is for callers that want the whole
        /// merge as a single operation.
        /// </summary>
        /// <param name="subgraph">Candidate subgraph.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="subjectId">Owning subject identifier.</param>
        /// <param name="sourceNodeId">Provenance Source node identifier, if one was created.</param>
        /// <param name="jobId">Ingestion job identifier that asserted the elements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created/resolved node and edge identifiers.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="subgraph"/> is null.</exception>
        public async Task<MergeResult> MergeAsync(
            CandidateSubgraph subgraph,
            string tenantId,
            string subjectId,
            string? sourceNodeId,
            string jobId,
            CancellationToken token = default)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));

            Canonicalize(subgraph);
            NodeMergeResult nodes = await MergeNodesAsync(subgraph, tenantId, subjectId, sourceNodeId, jobId, token).ConfigureAwait(false);
            EdgeConsolidationResult edges = await ConsolidateEdgesAsync(subgraph, nodes.RefToId, sourceNodeId, jobId, token).ConfigureAwait(false);

            return new MergeResult
            {
                NodeIds = nodes.NodeIds,
                EdgeIds = edges.EdgeIds,
                RefToId = nodes.RefToId,
                SourceNodeId = sourceNodeId
            };
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Find an existing edge of the given type between two nodes (in that direction), or null. Used to
        /// consolidate a re-asserted relationship rather than creating a duplicate.
        /// </summary>
        private async Task<GraphEdge?> FindExistingEdgeAsync(string fromId, string toId, string edgeType, CancellationToken token)
        {
            List<GraphEdge> edges = await _Graph.GetEdgesAsync(fromId, token).ConfigureAwait(false);
            foreach (GraphEdge edge in edges)
            {
                if (String.Equals(edge.FromNodeId, fromId, StringComparison.Ordinal)
                    && String.Equals(edge.ToNodeId, toId, StringComparison.Ordinal)
                    && String.Equals(edge.EdgeType, edgeType, StringComparison.Ordinal))
                {
                    return edge;
                }
            }
            return null;
        }

        private static double ReadTagDouble(GraphEdge edge, string key, double fallback)
        {
            if (edge.Tags != null && edge.Tags.TryGetValue(key, out string? raw) && Double.TryParse(raw, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out double value)) return value;
            return fallback;
        }

        private static int ReadTagInt(GraphEdge edge, string key, int fallback)
        {
            if (edge.Tags != null && edge.Tags.TryGetValue(key, out string? raw) && Int32.TryParse(raw, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) return value;
            return fallback;
        }

        private static GraphNode BuildNode(CandidateNode candidate, string canonical, string tenantId, string subjectId, string jobId, string? sourceNodeId)
        {
            GraphNode node = new GraphNode
            {
                NodeType = candidate.NodeType,
                Name = candidate.Name,
                CanonicalName = canonical,
                Content = candidate.Content,
                Labels = new List<string> { candidate.NodeType }
            };

            node.Tags[Ontology.TagTenantId] = tenantId;
            node.Tags[Ontology.TagSubjectId] = subjectId;
            node.Tags[Ontology.TagNodeType] = candidate.NodeType;
            node.Tags[Ontology.TagCanonicalName] = canonical;
            node.Tags[Ontology.TagConfidence] = candidate.Confidence.ToString("F3", CultureInfo.InvariantCulture);
            node.Tags[Ontology.TagAssertedByJob] = jobId;
            if (!String.IsNullOrEmpty(candidate.Rights)) node.Tags[Ontology.TagRights] = candidate.Rights!;
            if (!String.IsNullOrEmpty(candidate.Authority)) node.Tags[Ontology.TagAuthority] = candidate.Authority!;
            if (!String.IsNullOrEmpty(sourceNodeId)) node.Tags[Ontology.TagSourceId] = sourceNodeId!;

            return node;
        }

        private async Task CreateProvenanceEdgeAsync(string nodeId, string sourceNodeId, string jobId, CancellationToken token)
        {
            GraphEdge provenance = new GraphEdge
            {
                EdgeType = Ontology.EdgeDerivedFromSource,
                FromNodeId = nodeId,
                ToNodeId = sourceNodeId,
                Tags = new Dictionary<string, string> { { Ontology.TagAssertedByJob, jobId } }
            };
            await _Graph.CreateEdgeAsync(provenance, token).ConfigureAwait(false);
        }

        #endregion
    }
}
