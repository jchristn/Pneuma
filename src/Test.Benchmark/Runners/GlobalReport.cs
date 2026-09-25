namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// A global-vs-local thematic answering report.
    /// </summary>
    public class GlobalReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind.
        /// </summary>
        public string Kind { get; set; } = "global";

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
        /// Criterion to { global, local, tie } win rates.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> WinRates { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Judged items.
        /// </summary>
        public List<GlobalItem> Items { get; set; } = new List<GlobalItem>();

        #endregion
    }
}
