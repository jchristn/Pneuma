namespace Test.Benchmark.Agent
{
    /// <summary>
    /// One task run in one arm.
    /// </summary>
    public class AgentItem
    {
        #region Public-Members

        /// <summary>
        /// Task id.
        /// </summary>
        public string TaskId { get; set; } = string.Empty;

        /// <summary>
        /// Task type.
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// pneuma or none.
        /// </summary>
        public string Arm { get; set; } = string.Empty;

        /// <summary>
        /// True when every expected pattern matched and no forbidden one did.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Agent turns.
        /// </summary>
        public int Turns { get; set; } = 0;

        /// <summary>
        /// Reported cost.
        /// </summary>
        public double CostUsd { get; set; } = 0.0;

        /// <summary>
        /// Wall time.
        /// </summary>
        public double DurationMs { get; set; } = 0.0;

        /// <summary>
        /// Final answer.
        /// </summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>
        /// Error, when the run failed.
        /// </summary>
        public string? Error { get; set; } = null;

        #endregion
    }
}
