namespace Pneuma.Core.Graph
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// An Pneuma knowledge-graph node, mapped to a LiteGraph node.
    /// </summary>
    public class GraphNode
    {
        #region Public-Members

        /// <summary>LiteGraph node GUID.</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Node type (a label from the ontology).</summary>
        public string NodeType { get; set; } = String.Empty;

        /// <summary>Display name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Canonical name used for entity resolution.</summary>
        public string? CanonicalName { get; set; } = null;

        /// <summary>Free-text content (for example lyric text or a cell excerpt).</summary>
        public string? Content { get; set; } = null;

        /// <summary>Labels applied to the node.</summary>
        public List<string> Labels { get; set; } = new List<string>();

        /// <summary>Tags applied to the node (metadata, provenance, rights, authority, confidence).</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
