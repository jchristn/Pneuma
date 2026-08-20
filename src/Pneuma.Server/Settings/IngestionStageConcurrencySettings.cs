namespace Pneuma.Server.Settings
{
    using System;

    /// <summary>
    /// Per-stage concurrency caps for the ingestion pipeline. Independent of how many jobs run at once
    /// (<see cref="IngestionSettings.MaxConcurrentTasks"/>), these bound how many jobs may execute a given
    /// stage simultaneously — so hundreds of enqueued documents cannot overwhelm the model runners or other
    /// backends at the expensive steps (classification, summarization, embedding). Each value is clamped to
    /// [1, 256]; the model-runner-bound stages default lower than the cheap I/O stages.
    /// </summary>
    public class IngestionStageConcurrencySettings
    {
        #region Public-Members

        /// <summary>Concurrent type-detection calls (DocumentAtom). Default 8.</summary>
        public int TypeDetection
        {
            get { return _TypeDetection; }
            set { _TypeDetection = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent semantic-cell extractions (DocumentAtom). Default 8.</summary>
        public int CellExtraction
        {
            get { return _CellExtraction; }
            set { _CellExtraction = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent knowledge-graph classification calls (completion model runner). Default 4.</summary>
        public int Classification
        {
            get { return _Classification; }
            set { _Classification = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent graph-merge operations (LiteGraph). Default 8.</summary>
        public int GraphMerge
        {
            get { return _GraphMerge; }
            set { _GraphMerge = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent summarization calls (completion model runner). Default 4.</summary>
        public int Summarization
        {
            get { return _Summarization; }
            set { _Summarization = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent chunking calls (Partio). Default 8.</summary>
        public int Chunking
        {
            get { return _Chunking; }
            set { _Chunking = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent embedding calls (embedding model runner). Default 4.</summary>
        public int Embedding
        {
            get { return _Embedding; }
            set { _Embedding = Math.Clamp(value, 1, 256); }
        }

        /// <summary>Concurrent search-indexing operations (RecallDB). Default 8.</summary>
        public int Indexing
        {
            get { return _Indexing; }
            set { _Indexing = Math.Clamp(value, 1, 256); }
        }

        #endregion

        #region Private-Members

        private int _TypeDetection = 8;
        private int _CellExtraction = 8;
        private int _Classification = 4;
        private int _GraphMerge = 8;
        private int _Summarization = 4;
        private int _Chunking = 8;
        private int _Embedding = 4;
        private int _Indexing = 8;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Initialize per-stage concurrency settings with defaults.</summary>
        public IngestionStageConcurrencySettings()
        {
        }

        #endregion
    }
}
