namespace Test.Benchmark.Client
{
    /// <summary>
    /// One entry of the agentic chat tool trace.
    /// </summary>
    public class ChatToolCall
    {
        #region Public-Members

        /// <summary>
        /// Tool name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Ok { get; set; } = false;

        #endregion
    }
}
