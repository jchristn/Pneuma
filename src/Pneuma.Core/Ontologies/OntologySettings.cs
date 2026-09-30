namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>
    /// Server settings for ontology governance (the <c>Ontology</c> section of pneuma.json). These are system-wide
    /// resource limits: they protect memory, model capacity, and database size that every tenant shares, so a tenant
    /// cannot change them.
    /// </summary>
    public class OntologySettings
    {
        #region Public-Members

        /// <summary>Run queued ontology operations (validate, retag, drift check) on this server. Default true.</summary>
        public bool WorkerEnabled { get; set; } = true;

        /// <summary>Milliseconds between worker polls for queued operations. Default 2000; clamped to [250, 60000].</summary>
        public int WorkerPollIntervalMs
        {
            get { return _WorkerPollIntervalMs; }
            set { _WorkerPollIntervalMs = Math.Clamp(value, 250, 60000); }
        }

        /// <summary>The most nodes a graph export or a validation reads. Default 100000; clamped to [100, 1000000].</summary>
        public int MaxGraphNodes
        {
            get { return _MaxGraphNodes; }
            set { _MaxGraphNodes = Math.Clamp(value, 100, 1000000); }
        }

        /// <summary>The most violations recorded for one ingestion job; the rest are counted only. Default 500; clamped to [0, 10000].</summary>
        public int MaxViolationsPerJob
        {
            get { return _MaxViolationsPerJob; }
            set { _MaxViolationsPerJob = Math.Clamp(value, 0, 10000); }
        }

        /// <summary>The most cells an ontology proposal samples. Default 50; clamped to [1, 200].</summary>
        public int MaxProposalSampleCells
        {
            get { return _MaxProposalSampleCells; }
            set { _MaxProposalSampleCells = Math.Clamp(value, 1, 200); }
        }

        /// <summary>The most cells a drift check samples (each costs two model calls). Default 25; clamped to [1, 200].</summary>
        public int MaxDriftSampleSize
        {
            get { return _MaxDriftSampleSize; }
            set { _MaxDriftSampleSize = Math.Clamp(value, 1, 200); }
        }

        /// <summary>Days an unused classification cache entry is kept. Default 90; clamped to [1, 3650].</summary>
        public int CacheRetentionDays
        {
            get { return _CacheRetentionDays; }
            set { _CacheRetentionDays = Math.Clamp(value, 1, 3650); }
        }

        #endregion

        #region Private-Members

        private int _WorkerPollIntervalMs = 2000;
        private int _MaxGraphNodes = 100000;
        private int _MaxViolationsPerJob = 500;
        private int _MaxProposalSampleCells = 50;
        private int _MaxDriftSampleSize = 25;
        private int _CacheRetentionDays = 90;

        #endregion
    }
}
