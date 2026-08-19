namespace Pneuma.Core.Graph
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of merging a candidate subgraph: the node and edge identifiers created or reused.
    /// </summary>
    public class MergeResult
    {
        /// <summary>Identifiers of nodes created or resolved during merge.</summary>
        public List<string> NodeIds { get; set; } = new List<string>();

        /// <summary>Identifiers of edges created during merge.</summary>
        public List<string> EdgeIds { get; set; } = new List<string>();
    }
}
