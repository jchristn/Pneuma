namespace Pneuma.Sdk.Models
{
    /// <summary>
    /// Counts of what each ingestion stage received and produced for one job. A failed count greater than zero means
    /// work was dropped and a warning was recorded on the job.
    /// </summary>
    public class IngestionCompleteness
    {
        /// <summary>Cells produced by content extraction.</summary>
        public int CellsExtracted { get; set; } = 0;

        /// <summary>Classification batches attempted.</summary>
        public int ClassificationBatches { get; set; } = 0;

        /// <summary>Classification batches that failed and were skipped.</summary>
        public int ClassificationBatchesFailed { get; set; } = 0;

        /// <summary>Cell nodes created in the knowledge graph.</summary>
        public int CellNodesCreated { get; set; } = 0;

        /// <summary>Cell nodes that could not be created.</summary>
        public int CellNodesFailed { get; set; } = 0;

        /// <summary>Cell summaries attempted.</summary>
        public int SummariesAttempted { get; set; } = 0;

        /// <summary>Cell summaries that failed and were skipped.</summary>
        public int SummariesFailed { get; set; } = 0;

        /// <summary>Chunks produced by chunking.</summary>
        public int ChunksProduced { get; set; } = 0;

        /// <summary>Chunks that received an embedding.</summary>
        public int ChunksEmbedded { get; set; } = 0;

        /// <summary>Chunks stored in the search index.</summary>
        public int ChunksIndexed { get; set; } = 0;
    }
}
