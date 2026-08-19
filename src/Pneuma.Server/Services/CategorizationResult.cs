namespace Pneuma.Server.Services
{
    using System.Collections.Generic;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Integrations.Models;

    /// <summary>
    /// The output of the categorization phase of ingestion: the extracted semantic cells and the
    /// candidate subgraph (the proposed knowledge-graph plan) produced by classification. Handed to the
    /// hydration phase, which commits the plan to the graph, embeddings, and search index.
    /// </summary>
    public class CategorizationResult
    {
        #region Public-Members

        /// <summary>The semantic cells extracted from the source document.</summary>
        public List<ExtractedCell> Cells { get; set; } = new List<ExtractedCell>();

        /// <summary>The candidate subgraph (proposed nodes and relationships) to hydrate.</summary>
        public CandidateSubgraph Subgraph { get; set; } = new CandidateSubgraph();

        #endregion
    }
}
