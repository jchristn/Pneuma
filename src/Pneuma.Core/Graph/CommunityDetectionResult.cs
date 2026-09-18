namespace Pneuma.Core.Graph
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of running community detection (e.g. Louvain) over a tenant's graph: how many communities
    /// were found and, per node, which community it was assigned to. Community ids are opaque and stable only
    /// within a single run.
    /// </summary>
    public class CommunityDetectionResult
    {
        #region Public-Members

        /// <summary>Number of communities detected.</summary>
        public int CommunityCount { get; set; } = 0;

        /// <summary>Per-node community assignments.</summary>
        public List<NodeCommunity> Nodes { get; set; } = new List<NodeCommunity>();

        #endregion
    }
}
