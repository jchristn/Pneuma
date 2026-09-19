namespace Pneuma.Core.Models
{
    using System;

    /// <summary>
    /// The system-wide (global) ingestion concurrency and pipeline tuning defaults. A single persisted
    /// singleton row (<see cref="DefaultId"/>) whose values seed the runtime limiters and are editable at
    /// runtime; per-subject overrides layer on top (see <c>Subject.ConcurrencyOverrides</c>). Every numeric
    /// property clamps to the same range the corresponding startup setting used.
    /// </summary>
    public class IngestionTuning
    {
        #region Public-Members

        /// <summary>The fixed identifier of the singleton tuning row.</summary>
        public const string DefaultId = "itune_default";

        /// <summary>Row identifier (always <see cref="DefaultId"/>).</summary>
        public string Id { get; set; } = DefaultId;

        /// <summary>Concurrent content-retrieval fetches. Clamped to [1, 256].</summary>
        public int ContentRetrieval { get => _ContentRetrieval; set => _ContentRetrieval = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent type-detection calls. Clamped to [1, 256].</summary>
        public int TypeDetection { get => _TypeDetection; set => _TypeDetection = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent cell-extraction calls. Clamped to [1, 256].</summary>
        public int CellExtraction { get => _CellExtraction; set => _CellExtraction = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent classification calls (completion model). Clamped to [1, 256].</summary>
        public int Classification { get => _Classification; set => _Classification = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent graph-merge / relationship-consolidation operations. Clamped to [1, 256].</summary>
        public int GraphMerge { get => _GraphMerge; set => _GraphMerge = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent summarization stage slots. Clamped to [1, 256].</summary>
        public int Summarization { get => _Summarization; set => _Summarization = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent chunking operations. Clamped to [1, 256].</summary>
        public int Chunking { get => _Chunking; set => _Chunking = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent embedding calls (embedding model). Clamped to [1, 256].</summary>
        public int Embedding { get => _Embedding; set => _Embedding = Math.Clamp(value, 1, 256); }

        /// <summary>Concurrent search-indexing operations. Clamped to [1, 256].</summary>
        public int Indexing { get => _Indexing; set => _Indexing = Math.Clamp(value, 1, 256); }

        /// <summary>Number of ingestion jobs that may process concurrently. Clamped to [1, 64].</summary>
        public int MaxConcurrentTasks { get => _MaxConcurrentTasks; set => _MaxConcurrentTasks = Math.Clamp(value, 1, 64); }

        /// <summary>Maximum cells summarized concurrently within a single job. Clamped to [1, 64].</summary>
        public int SummarizationConcurrency { get => _SummarizationConcurrency; set => _SummarizationConcurrency = Math.Clamp(value, 1, 64); }

        /// <summary>Minimum trimmed cell length (characters) to summarize; shorter cells are skipped. Clamped to [0, 100000].</summary>
        public int SummarizationMinCellLength { get => _SummarizationMinCellLength; set => _SummarizationMinCellLength = Math.Clamp(value, 0, 100000); }

        /// <summary>Per-stage timeout, in seconds. Clamped to [5, 3600].</summary>
        public int StageTimeoutSeconds { get => _StageTimeoutSeconds; set => _StageTimeoutSeconds = Math.Clamp(value, 5, 3600); }

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private int _ContentRetrieval = 4;
        private int _TypeDetection = 8;
        private int _CellExtraction = 8;
        private int _Classification = 4;
        private int _GraphMerge = 8;
        private int _Summarization = 4;
        private int _Chunking = 8;
        private int _Embedding = 4;
        private int _Indexing = 8;
        private int _MaxConcurrentTasks = 4;
        private int _SummarizationConcurrency = 4;
        private int _SummarizationMinCellLength = 128;
        private int _StageTimeoutSeconds = 900;

        #endregion
    }
}
