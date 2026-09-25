namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;

    /// <summary>
    /// A suite of agent tasks over one dataset.
    /// </summary>
    public class AgentTaskFile
    {
        #region Public-Members

        /// <summary>
        /// Suite name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Dataset path, relative to the task file.
        /// </summary>
        public string Dataset { get; set; } = string.Empty;

        /// <summary>
        /// Tasks.
        /// </summary>
        public List<AgentTask> Tasks { get; set; } = new List<AgentTask>();

        #endregion
    }
}
