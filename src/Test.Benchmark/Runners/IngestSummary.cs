namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;
    using Test.Benchmark.Metrics;

    /// <summary>
    /// What provisioning did: documents ingested, reused subjects, failures by stage, and ingest throughput.
    /// Any failure marks the run as not comparable (a silently lost document skews every metric).
    /// </summary>
    public class IngestSummary
    {
        #region Public-Members

        /// <summary>
        /// Documents across all corpora.
        /// </summary>
        public int Documents { get; set; } = 0;

        /// <summary>
        /// Documents submitted in this run (0 when every subject was reused).
        /// </summary>
        public int Submitted { get; set; } = 0;

        /// <summary>
        /// Documents ingested successfully (including reused ones).
        /// </summary>
        public int Succeeded { get; set; } = 0;

        /// <summary>
        /// Documents that failed or never finished.
        /// </summary>
        public int Failures { get; set; } = 0;

        /// <summary>
        /// Subjects reused from an earlier run.
        /// </summary>
        public int ReusedSubjects { get; set; } = 0;

        /// <summary>
        /// Wall-clock time spent ingesting in this run.
        /// </summary>
        public double WallSeconds { get; set; } = 0.0;

        /// <summary>
        /// Submitted documents per second.
        /// </summary>
        public double DocumentsPerSecond { get; set; } = 0.0;

        /// <summary>
        /// Failures by the stage they failed in.
        /// </summary>
        public Dictionary<string, int> FailuresByStage { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Up to 20 failed documents with their error.
        /// </summary>
        public List<string> FailureSamples { get; set; } = new List<string>();

        /// <summary>
        /// Server-side ingestion stage timings during this run.
        /// </summary>
        public Dictionary<string, StageBreakdown> Stages { get; set; } = new Dictionary<string, StageBreakdown>();

        /// <summary>
        /// True when every document is ingested.
        /// </summary>
        public bool Complete
        {
            get
            {
                return Failures == 0 && Succeeded == Documents;
            }
        }

        #endregion
    }
}
