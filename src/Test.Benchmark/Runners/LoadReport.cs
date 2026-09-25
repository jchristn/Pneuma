namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// A load benchmark report.
    /// </summary>
    public class LoadReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind.
        /// </summary>
        public string Kind { get; set; } = "load";

        /// <summary>
        /// Dataset the searched subject was built from.
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
        /// Ingest summary.
        /// </summary>
        public IngestSummary Ingest { get; set; } = new IngestSummary();

        /// <summary>
        /// Results per concurrency level.
        /// </summary>
        public List<LoadLevel> Levels { get; set; } = new List<LoadLevel>();

        #endregion
    }
}
