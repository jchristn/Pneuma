namespace Pneuma.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A cell summary produced during ingestion, paired with the graph Cell node it was derived from so the
    /// chunks it produces resolve back to that cell in the knowledge graph.
    /// </summary>
    public class CellSummary
    {
        /// <summary>Identifier of the graph Cell node this summary was derived from (empty when unknown).</summary>
        public string CellNodeId { get; set; } = String.Empty;

        /// <summary>The summary text.</summary>
        public string Text { get; set; } = String.Empty;
    }
}
