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

        /// <summary>
        /// Identifiers of the Cell nodes created for the source's extracted cells, aligned one-to-one with the
        /// cells supplied to the merge (empty string where a cell had no text and produced no node). Chunks
        /// derived from a cell carry its node id so their RecallDB documents resolve back to it.
        /// </summary>
        public List<string> CellNodeIds { get; set; } = new List<string>();
    }
}
