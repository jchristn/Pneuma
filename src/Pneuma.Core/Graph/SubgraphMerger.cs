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
        /// Merge a candidate subgraph into the live graph.
        /// </summary>
        /// <param name="subgraph">Candidate subgraph.</param>
        /// <param name="tenantId">Owning tenant identifier.</param>
        /// <param name="subjectId">Owning subject identifier.</param>
        /// <param name="sourceNodeId">Provenance Source node identifier, if one was created.</param>
        /// <param name="jobId">Ingestion job identifier that asserted the elements.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The created/resolved node and edge identifiers.</returns>
        public async Task<MergeResult> MergeAsync(
            CandidateSubgraph subgraph,
            string tenantId,
            string subjectId,
            string? sourceNodeId,
            string jobId,
            CancellationToken token = default)
        {
            if (subgraph == null) throw new ArgumentNullException(nameof(subgraph));

            MergeResult result = new MergeResult();
            Dictionary<string, string> refToId = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (CandidateNode candidate in subgraph.Nodes)
            {
                // The ontology is admin-defined in natural language, so accept any non-empty node type
                // the model emits (it becomes a LiteGraph label) rather than gating on the seeded set. It is
                // canonicalized first (coerced to a built-in type when recognized, otherwise whitespace-collapsed)
                // so casing/spelling variance does not fragment the ontology into near-duplicate labels.
                if (String.IsNullOrWhiteSpace(candidate.NodeType) || String.IsNullOrWhiteSpace(candidate.Name)) continue;
                candidate.NodeType = Ontology.CanonicalNodeType(candidate.NodeType);

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

                if (!String.IsNullOrEmpty(candidate.Ref) && !refToId.ContainsKey(candidate.Ref))
                {
                    refToId[candidate.Ref] = nodeId;
                }
                if (!result.NodeIds.Contains(nodeId)) result.NodeIds.Add(nodeId);
            }

            foreach (CandidateEdge candidate in subgraph.Edges)
            {
                if (String.IsNullOrWhiteSpace(candidate.EdgeType)) continue;
                // Canonicalize the relationship type (built-in when recognized, else UPPER_SNAKE) to curb drift.
                candidate.EdgeType = Ontology.CanonicalEdgeType(candidate.EdgeType);
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
                if (!String.IsNullOrEmpty(created.Id)) result.EdgeIds.Add(created.Id);
            }

            return result;
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
