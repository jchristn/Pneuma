namespace Pneuma.Core.Graph
{
    /// <summary>A single node's community assignment from community detection.</summary>
    public class NodeCommunity
    {
        /// <summary>The node identifier.</summary>
        public string NodeId { get; set; } = string.Empty;

        /// <summary>The node's name (as reported by the algorithm), for building summaries without a second read.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The assigned community id (opaque; stable only within one run).</summary>
        public long Community { get; set; } = -1;
    }
}
