namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Graph;

    /// <summary>A subject's stored graph: its nodes and the edges among them.</summary>
    public class SubjectGraph
    {
        #region Public-Members

        /// <summary>The subject's nodes.</summary>
        public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();

        /// <summary>The edges whose endpoints are both among the nodes.</summary>
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();

        /// <summary>Whether the node limit was reached, so the graph may be incomplete.</summary>
        public bool Truncated { get; set; } = false;

        #endregion
    }
}
