namespace Test.Benchmark.Runners
{
    /// <summary>
    /// Ingest fidelity measurements for one document.
    /// </summary>
    public class IngestDocument
    {
        #region Public-Members

        /// <summary>
        /// Document id.
        /// </summary>
        public string DocumentId { get; set; } = string.Empty;

        /// <summary>
        /// Format served.
        /// </summary>
        public string Format { get; set; } = "md";

        /// <summary>
        /// True when the atoms and chunks artifacts were both available.
        /// </summary>
        public bool Measured { get; set; } = false;

        /// <summary>
        /// Cells extracted.
        /// </summary>
        public int Cells { get; set; } = 0;

        /// <summary>
        /// Chunks stored.
        /// </summary>
        public int Chunks { get; set; } = 0;

        /// <summary>
        /// Chunks cut from cell text.
        /// </summary>
        public int ContentChunks { get; set; } = 0;

        /// <summary>
        /// Chunks cut from LLM summaries (text not found in the cells).
        /// </summary>
        public int SummaryChunks { get; set; } = 0;

        /// <summary>
        /// Chunks wholly contained in the chunk before them.
        /// </summary>
        public int RedundantChunks { get; set; } = 0;

        /// <summary>
        /// U+FFFD replacement characters found in stored chunks (text corrupted by decoding).
        /// </summary>
        public int ReplacementCharacters { get; set; } = 0;

        /// <summary>
        /// Share of the source document's distinct words present in the extracted cells.
        /// </summary>
        public double ExtractionCoverage { get; set; } = 0.0;

        /// <summary>
        /// Mean words per content chunk.
        /// </summary>
        public double MeanChunkWords { get; set; } = 0.0;

        #endregion
    }
}
