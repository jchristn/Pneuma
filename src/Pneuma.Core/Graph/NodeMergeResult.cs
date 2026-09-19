namespace Pneuma.Core.Graph
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of merging a candidate subgraph's nodes: the created/resolved node identifiers and the
    /// map from candidate node reference to its resolved graph node id (used by the relationship-consolidation
    /// step to wire edges).
    /// </summary>
    public class NodeMergeResult
    {
        /// <summary>Identifiers of nodes created or resolved during the node merge.</summary>
        public List<string> NodeIds { get; set; } = new List<string>();

        /// <summary>Map from a candidate node's <c>Ref</c> to its resolved graph node id.</summary>
        public Dictionary<string, string> RefToId { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    }
}
