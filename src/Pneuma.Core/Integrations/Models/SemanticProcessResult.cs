namespace Pneuma.Core.Integrations.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of a semantic process call: chunks of the cell (and, when requested, of a summary).
    /// </summary>
    public class SemanticProcessResult
    {
        /// <summary>Chunks of the cell content.</summary>
        public List<SemanticChunk> Chunks { get; set; } = new List<SemanticChunk>();

        /// <summary>Chunks of the generated summary, when summarization was requested.</summary>
        public List<SemanticChunk> SummaryChunks { get; set; } = new List<SemanticChunk>();

        /// <summary>The generated summary text, when summarization was requested.</summary>
        public string? Summary { get; set; } = null;
    }
}
