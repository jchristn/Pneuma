namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// An ingest fidelity report.
    /// </summary>
    public class IngestReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind.
        /// </summary>
        public string Kind { get; set; } = "ingest";

        /// <summary>
        /// Dataset name.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// Optional run label.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Ingest summary (success, failures by stage, stage timings).
        /// </summary>
        public IngestSummary Ingest { get; set; } = new IngestSummary();

        /// <summary>
        /// Aggregate fidelity metrics.
        /// </summary>
        public Dictionary<string, double> Summary { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Aggregate metrics per served format.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> ByFormat { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Per-document measurements.
        /// </summary>
        public List<IngestDocument> Documents { get; set; } = new List<IngestDocument>();

        #endregion
    }
}
