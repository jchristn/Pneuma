namespace Pneuma.Core.Graph
{
    using System.Collections.Generic;

    /// <summary>
    /// A candidate subgraph produced by ontology classification, ready to merge into the live graph.
    /// </summary>
    public class CandidateSubgraph
    {
        #region Public-Members

        /// <summary>Candidate nodes.</summary>
        public List<CandidateNode> Nodes { get; set; } = new List<CandidateNode>();

        /// <summary>Candidate edges referencing nodes by local Ref.</summary>
        public List<CandidateEdge> Edges { get; set; } = new List<CandidateEdge>();

        #endregion
    }
}
