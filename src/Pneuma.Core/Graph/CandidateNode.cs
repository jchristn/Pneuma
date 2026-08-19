namespace Pneuma.Core.Graph
{
    using System;

    /// <summary>
    /// A node in a model-produced candidate subgraph, before merge into the live graph.
    /// </summary>
    public class CandidateNode
    {
        #region Public-Members

        /// <summary>Local reference identifier, used by candidate edges before real GUIDs exist.</summary>
        public string Ref { get; set; } = String.Empty;

        /// <summary>Node type (a label from the ontology).</summary>
        public string NodeType { get; set; } = String.Empty;

        /// <summary>Display name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Canonical name for entity resolution. Defaults to Name when omitted.</summary>
        public string? CanonicalName { get; set; } = null;

        /// <summary>Free-text content.</summary>
        public string? Content { get; set; } = null;

        /// <summary>Rights classification (for example SubjectOwned, PublicDomain, Restricted).</summary>
        public string? Rights { get; set; } = null;

        /// <summary>Authority classification (for example Canonical, SubjectValidated, ThirdParty).</summary>
        public string? Authority { get; set; } = null;

        /// <summary>Model confidence (0..1).</summary>
        public double Confidence { get; set; } = 0.5;

        #endregion
    }
}
