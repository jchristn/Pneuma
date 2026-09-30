namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;
    using Pneuma.Core.Integrations.Abstractions;

    /// <summary>
    /// Reads a subject's stored graph: every node tagged with the subject (up to a limit) and the edges among them. Edges
    /// are read per node with bounded concurrency, since edges carry no subject tag.
    /// </summary>
    public static class SubjectGraphReader
    {
        #region Public-Methods

        /// <summary>Read a subject's graph.</summary>
        /// <param name="graph">The tenant's graph.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="maxNodes">The most nodes to read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> is null.</exception>
        public static async Task<SubjectGraph> ReadAsync(IGraphRepository graph, string subjectId, int maxNodes, CancellationToken token = default)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            SubjectGraph result = new SubjectGraph();
            int limit = Math.Max(1, maxNodes);
            Dictionary<string, string> tags = new Dictionary<string, string> { { Ontology.TagSubjectId, subjectId } };
            List<GraphNode> nodes = await graph.SearchNodesByTagsAsync(tags, limit + 1, token).ConfigureAwait(false);
            if (nodes.Count > limit)
            {
                result.Truncated = true;
                nodes = nodes.GetRange(0, limit);
            }
            result.Nodes = nodes;

            HashSet<string> nodeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphNode node in nodes)
            {
                if (!String.IsNullOrEmpty(node.Id)) nodeIds.Add(node.Id);
            }

            Dictionary<string, GraphEdge> edges = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
            object sync = new object();
            ParallelOptions options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token };
            await Parallel.ForEachAsync(nodeIds, options, async (id, ct) =>
            {
                List<GraphEdge> attached = await graph.GetEdgesAsync(id, ct).ConfigureAwait(false);
                lock (sync)
                {
                    foreach (GraphEdge edge in attached)
                    {
                        if (String.IsNullOrEmpty(edge.Id) || edges.ContainsKey(edge.Id)) continue;
                        if (!nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId)) continue;
                        edges[edge.Id] = edge;
                    }
                }
            }).ConfigureAwait(false);
            result.Edges = new List<GraphEdge>(edges.Values);
            return result;
        }

        /// <summary>Read a subject's Cell nodes (the graph's unit of source text).</summary>
        /// <param name="graph">The tenant's graph.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="maxNodes">The most cells to read.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The cells.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="graph"/> is null.</exception>
        public static Task<List<GraphNode>> ReadCellsAsync(IGraphRepository graph, string subjectId, int maxNodes, CancellationToken token = default)
        {
            if (graph == null) throw new ArgumentNullException(nameof(graph));
            Dictionary<string, string> tags = new Dictionary<string, string>
            {
                { Ontology.TagSubjectId, subjectId },
                { Ontology.TagNodeType, Ontology.NodeCell }
            };
            return graph.SearchNodesByTagsAsync(tags, Math.Max(1, maxNodes), token);
        }

        #endregion
    }
}
