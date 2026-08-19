namespace Pneuma.Core.Graph
{
    using System;

    /// <summary>
    /// An edge in a model-produced candidate subgraph, referencing candidate nodes by their local Ref.
    /// </summary>
    public class CandidateEdge
    {
        #region Public-Members

        /// <summary>Local reference of the source candidate node.</summary>
        public string FromRef { get; set; } = String.Empty;

        /// <summary>Local reference of the target candidate node.</summary>
        public string ToRef { get; set; } = String.Empty;

        /// <summary>Edge type (a relationship label from the ontology).</summary>
        public string EdgeType { get; set; } = String.Empty;

        /// <summary>Model confidence (0..1).</summary>
        public double Confidence { get; set; } = 0.5;

        #endregion
    }
}
