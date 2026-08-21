namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A chunk produced by Partio, with its embedding vector.
    /// </summary>
    public class PartioChunk
    {
        /// <summary>Chunk text.</summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>Embedding vector.</summary>
        public List<float> Embeddings { get; set; } = new List<float>();

        /// <summary>
        /// Identifier of the graph Cell node this chunk was derived from, if any. The chunk itself is not a
        /// graph node; this is stored as the chunk document's <c>litegraphNodeId</c> tag so a retrieval hit
        /// resolves back to its originating cell in the knowledge graph.
        /// </summary>
        public string? CellNodeId { get; set; } = null;
    }
}
