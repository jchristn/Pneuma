namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An Pneuma knowledge-graph edge.
    /// </summary>
    public class GraphEdge
    {
        /// <summary>Edge GUID.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Edge type (a relationship label from the ontology).</summary>
        public string EdgeType { get; set; } = string.Empty;

        /// <summary>Source node GUID.</summary>
        public string FromNodeId { get; set; } = string.Empty;

        /// <summary>Target node GUID.</summary>
        public string ToNodeId { get; set; } = string.Empty;

        /// <summary>Tags applied to the edge.</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
