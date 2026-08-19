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
    }
}
