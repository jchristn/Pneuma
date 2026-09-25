namespace Test.Benchmark.Agent
{
    using System.Collections.Generic;

    /// <summary>
    /// One agent task: a prompt whose correct answer lives in the corpus, graded by regular expressions.
    /// </summary>
    public class AgentTask
    {
        #region Public-Members

        /// <summary>
        /// Task id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Task type (recall, how-to, negative, ...).
        /// </summary>
        public string Type { get; set; } = "recall";

        /// <summary>
        /// The prompt given to the agent.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Case-insensitive patterns that must all appear in the answer.
        /// </summary>
        public List<string> Expect { get; set; } = new List<string>();

        /// <summary>
        /// Case-insensitive patterns that must not appear in the answer.
        /// </summary>
        public List<string> Forbid { get; set; } = new List<string>();

        #endregion
    }
}
