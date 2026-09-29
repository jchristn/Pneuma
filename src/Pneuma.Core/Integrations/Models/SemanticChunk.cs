namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A text chunk with its embedding vector.
    /// </summary>
    public class SemanticChunk
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

        /// <summary>
        /// What the chunk was cut from: "content" (the cell's own text) or "summary" (the cell's LLM summary).
        /// Stamped on the stored chunk as the <c>chunkKind</c> tag so retrieval can tell the two apart.
        /// </summary>
        public string Kind { get; set; } = "content";

        /// <summary>
        /// The text that is embedded when it differs from <see cref="Text"/>: the chunk with its context header (the
        /// document title and section headings) in front. Null embeds <see cref="Text"/> as is. Never stored or returned.
        /// </summary>
        public string? EmbeddingText { get; set; } = null;

        /// <summary>The context header this chunk was embedded with, or null (used to re-chunk with the same header).</summary>
        public string? Header { get; set; } = null;
    }
}
