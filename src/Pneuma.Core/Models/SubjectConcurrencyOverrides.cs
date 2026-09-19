namespace Pneuma.Core.Models
{
    using System;

    /// <summary>
    /// A subject's optional overrides for the ingestion tuning values. Every field is nullable: a null value
    /// means "inherit the system default" (see <see cref="IngestionTuning"/>); a set value replaces the system
    /// default for that subject's jobs. Persisted as JSON on the subject and applied to the runtime limiters.
    /// </summary>
    public class SubjectConcurrencyOverrides
    {
        #region Public-Members

        /// <summary>Override for concurrent content-retrieval fetches, or null to inherit.</summary>
        public int? ContentRetrieval { get; set; } = null;

        /// <summary>Override for concurrent type-detection calls, or null to inherit.</summary>
        public int? TypeDetection { get; set; } = null;

        /// <summary>Override for concurrent cell-extraction calls, or null to inherit.</summary>
        public int? CellExtraction { get; set; } = null;

        /// <summary>Override for concurrent classification calls, or null to inherit.</summary>
        public int? Classification { get; set; } = null;

        /// <summary>Override for concurrent graph-merge / relationship-consolidation operations, or null to inherit.</summary>
        public int? GraphMerge { get; set; } = null;

        /// <summary>Override for concurrent summarization stage slots, or null to inherit.</summary>
        public int? Summarization { get; set; } = null;

        /// <summary>Override for concurrent chunking operations, or null to inherit.</summary>
        public int? Chunking { get; set; } = null;

        /// <summary>Override for concurrent embedding calls, or null to inherit.</summary>
        public int? Embedding { get; set; } = null;

        /// <summary>Override for concurrent search-indexing operations, or null to inherit.</summary>
        public int? Indexing { get; set; } = null;

        /// <summary>Override for concurrent cells summarized within a single job, or null to inherit.</summary>
        public int? SummarizationConcurrency { get; set; } = null;

        /// <summary>Override for the minimum cell length to summarize, or null to inherit.</summary>
        public int? SummarizationMinCellLength { get; set; } = null;

        /// <summary>Override for the number of cells classified per model call, or null to inherit.</summary>
        public int? ClassificationBatchSize { get; set; } = null;

        /// <summary>Override for the classification batch context overlap (cells per side), or null to inherit.</summary>
        public int? ClassificationBatchOverlap { get; set; } = null;

        /// <summary>Override for the number of classification batches processed concurrently within a job, or null to inherit.</summary>
        public int? ClassificationBatchConcurrency { get; set; } = null;

        /// <summary>Override for the per-stage timeout in seconds, or null to inherit.</summary>
        public int? StageTimeoutSeconds { get; set; } = null;

        #endregion

        #region Public-Methods

        /// <summary>Whether every override is null (i.e. the subject overrides nothing).</summary>
        /// <returns>True when no field is set.</returns>
        public bool IsEmpty()
        {
            return ContentRetrieval == null && TypeDetection == null && CellExtraction == null
                && Classification == null && GraphMerge == null && Summarization == null
                && Chunking == null && Embedding == null && Indexing == null
                && SummarizationConcurrency == null && SummarizationMinCellLength == null
                && ClassificationBatchSize == null && ClassificationBatchOverlap == null
                && ClassificationBatchConcurrency == null
                && StageTimeoutSeconds == null;
        }

        #endregion
    }
}
