namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Graph;

    /// <summary>A subject's graph in Pneuma JSON export form.</summary>
    public class GraphExportDocument
    {
        #region Public-Members

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>Subject display name.</summary>
        public string SubjectName { get; set; } = String.Empty;

        /// <summary>The ontology version the subject was pinned to at export time, if any.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>UTC export time.</summary>
        public DateTime ExportedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>Whether the node limit was reached, so the export may be incomplete.</summary>
        public bool Truncated { get; set; } = false;

        /// <summary>The nodes.</summary>
        public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();

        /// <summary>The edges among them.</summary>
        public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();

        #endregion
    }
}
