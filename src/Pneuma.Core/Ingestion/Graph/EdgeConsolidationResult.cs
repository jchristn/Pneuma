namespace Pneuma.Core.Ingestion.Graph
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of consolidating a candidate subgraph's relationships: the identifiers of edges created or
    /// updated, and how many were newly created versus consolidated into an existing (re-asserted) edge.
    /// </summary>
    public class EdgeConsolidationResult
    {
        /// <summary>Identifiers of edges created or updated during consolidation.</summary>
        public List<string> EdgeIds { get; set; } = new List<string>();

        /// <summary>Number of new relationships created (no pre-existing edge of the same from/to/type).</summary>
        public int CreatedCount { get; set; } = 0;

        /// <summary>Number of re-asserted relationships consolidated into an existing edge (noisy-OR weight).</summary>
        public int ConsolidatedCount { get; set; } = 0;
    }
}
