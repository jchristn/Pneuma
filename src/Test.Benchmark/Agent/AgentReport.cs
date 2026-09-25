namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;
    using Test.Benchmark.Runners;

    /// <summary>
    /// An agent benchmark report.
    /// </summary>
    public class AgentReport
    {
        #region Public-Members

        /// <summary>
        /// Report kind.
        /// </summary>
        public string Kind { get; set; } = "agent";

        /// <summary>
        /// Suite name.
        /// </summary>
        public string Suite { get; set; } = string.Empty;

        /// <summary>
        /// Environment.
        /// </summary>
        public BenchmarkEnvironment Environment { get; set; } = new BenchmarkEnvironment();

        /// <summary>
        /// Configuration.
        /// </summary>
        public Dictionary<string, string> Config { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Arm to metrics.
        /// </summary>
        public Dictionary<string, Dictionary<string, double>> Arms { get; set; } = new Dictionary<string, Dictionary<string, double>>();

        /// <summary>
        /// Items.
        /// </summary>
        public List<AgentItem> Items { get; set; } = new List<AgentItem>();

        #endregion
    }
}
