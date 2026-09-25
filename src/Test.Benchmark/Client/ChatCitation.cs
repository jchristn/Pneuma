namespace Test.Benchmark.Client
{
    /// <summary>
    /// A link-level citation from the agentic chat <c>complete</c> event.
    /// </summary>
    public class ChatCitation
    {
        #region Public-Members

        /// <summary>
        /// Cited link id.
        /// </summary>
        public string? LinkId { get; set; } = null;

        /// <summary>
        /// Link URL.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Relevance score recorded for the citation.
        /// </summary>
        public double Score { get; set; } = 0.0;

        #endregion
    }
}
