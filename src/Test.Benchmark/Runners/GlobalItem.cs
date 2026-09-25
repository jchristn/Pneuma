namespace Test.Benchmark.Runners
{
    using System.Collections.Generic;

    /// <summary>
    /// One thematic question answered in global (community-summary) and local (chunk-retrieval) mode and judged
    /// pairwise.
    /// </summary>
    public class GlobalItem
    {
        #region Public-Members

        /// <summary>
        /// Query id.
        /// </summary>
        public string QueryId { get; set; } = string.Empty;

        /// <summary>
        /// Question.
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Answer from <c>/v1.0/query/global</c>.
        /// </summary>
        public string GlobalAnswer { get; set; } = string.Empty;

        /// <summary>
        /// Answer from <c>/v1.0/query</c>.
        /// </summary>
        public string LocalAnswer { get; set; } = string.Empty;

        /// <summary>
        /// Criterion to verdict: global, local, or tie (a split between the two presentation orders is a tie).
        /// </summary>
        public Dictionary<string, string> Verdicts { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
