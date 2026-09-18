namespace Pneuma.Core.Graph
{
    using System.Collections.Generic;

    /// <summary>
    /// A bounded region of the knowledge graph returned by a depth-limited subgraph extraction: the nodes
    /// reached within the requested hop depth and the edges among them. Used for multi-hop retrieval
    /// expansion.
    /// </summary>
    public class GraphSubgraph
    {
        #region Public-Members

        /// <summary>The nodes in the extracted subgraph.</summary>
        public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();

        /// <summary>The edges among the extracted nodes.</summary>
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();

        #endregion
    }
}
