namespace Pneuma.Core.Integrations.Models
{
    using System.Collections.Generic;

    /// <summary>
    /// The result of a Partio process call: chunks of the cell (and, when requested, of a summary).
    /// </summary>
    public class PartioProcessResult
    {
        /// <summary>Chunks of the cell content.</summary>
        public List<PartioChunk> Chunks { get; set; } = new List<PartioChunk>();

        /// <summary>Chunks of the generated summary, when summarization was requested.</summary>
        public List<PartioChunk> SummaryChunks { get; set; } = new List<PartioChunk>();

        /// <summary>The generated summary text, when summarization was requested.</summary>
        public string? Summary { get; set; } = null;
    }
}
