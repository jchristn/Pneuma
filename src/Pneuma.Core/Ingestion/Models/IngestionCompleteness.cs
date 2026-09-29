namespace Pneuma.Core.Ingestion.Models
{
    using System.Threading;

    /// <summary>
    /// Counts of what each stage received and produced for one job, so an operator can tell a complete ingest from a
    /// partial one. Stages running concurrent work increment the counters through the <c>Increment*</c> methods,
    /// which are thread-safe; the properties themselves are plain values for persistence and serialization.
    /// </summary>
    public class IngestionCompleteness
    {
        #region Public-Members

        /// <summary>Cells produced by content extraction.</summary>
        public int CellsExtracted { get; set; } = 0;

        /// <summary>Classification batches attempted.</summary>
        public int ClassificationBatches { get; set; } = 0;

        /// <summary>Classification batches that failed and were skipped.</summary>
        public int ClassificationBatchesFailed
        {
            get { return _ClassificationBatchesFailed; }
            set { _ClassificationBatchesFailed = value; }
        }

        /// <summary>Cell nodes created in the knowledge graph.</summary>
        public int CellNodesCreated
        {
            get { return _CellNodesCreated; }
            set { _CellNodesCreated = value; }
        }

        /// <summary>Cell nodes that could not be created.</summary>
        public int CellNodesFailed
        {
            get { return _CellNodesFailed; }
            set { _CellNodesFailed = value; }
        }

        /// <summary>Cell summaries attempted.</summary>
        public int SummariesAttempted { get; set; } = 0;

        /// <summary>Cell summaries that failed and were skipped.</summary>
        public int SummariesFailed
        {
            get { return _SummariesFailed; }
            set { _SummariesFailed = value; }
        }

        /// <summary>Chunks produced by chunking (content and summary chunks).</summary>
        public int ChunksProduced { get; set; } = 0;

        /// <summary>Chunks that received an embedding.</summary>
        public int ChunksEmbedded { get; set; } = 0;

        /// <summary>Chunks stored in the search index.</summary>
        public int ChunksIndexed { get; set; } = 0;

        #endregion

        #region Private-Members

        private int _ClassificationBatchesFailed = 0;
        private int _CellNodesCreated = 0;
        private int _CellNodesFailed = 0;
        private int _SummariesFailed = 0;

        #endregion

        #region Public-Methods

        /// <summary>Atomically count a failed classification batch.</summary>
        public void IncrementClassificationBatchesFailed()
        {
            Interlocked.Increment(ref _ClassificationBatchesFailed);
        }

        /// <summary>Atomically count a created cell node.</summary>
        public void IncrementCellNodesCreated()
        {
            Interlocked.Increment(ref _CellNodesCreated);
        }

        /// <summary>Atomically count a cell node that could not be created.</summary>
        public void IncrementCellNodesFailed()
        {
            Interlocked.Increment(ref _CellNodesFailed);
        }

        /// <summary>Atomically count a failed summary.</summary>
        public void IncrementSummariesFailed()
        {
            Interlocked.Increment(ref _SummariesFailed);
        }

        #endregion
    }
}
