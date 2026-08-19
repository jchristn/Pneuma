namespace Pneuma.Core.Graph
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An Pneuma knowledge-graph edge, mapped to a LiteGraph edge.
    /// </summary>
    public class GraphEdge
    {
        #region Public-Members

        /// <summary>LiteGraph edge GUID.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Edge type (a relationship label from the ontology).</summary>
        public string EdgeType { get; set; } = String.Empty;

        /// <summary>Source node GUID.</summary>
        public string FromNodeId { get; set; } = String.Empty;

        /// <summary>Target node GUID.</summary>
        public string ToNodeId { get; set; } = String.Empty;

        /// <summary>Tags applied to the edge.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
