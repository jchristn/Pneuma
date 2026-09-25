namespace Test.Benchmark.Client
{
    using System.Collections.Generic;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Options for a subject search.
    /// </summary>
    public class SearchOptions
    {
        #region Public-Members

        /// <summary>
        /// text, vector, or hybrid.
        /// </summary>
        public string Mode { get; set; } = "hybrid";

        /// <summary>
        /// Results to return.
        /// </summary>
        public int MaxResults { get; set; } = 10;

        /// <summary>
        /// Optional metadata filter.
        /// </summary>
        public QueryFilter? Filter { get; set; } = null;

        /// <summary>
        /// Extra query-string parameters (for example per-request retrieval overrides or granularity=chunk).
        /// </summary>
        public Dictionary<string, string> Extra { get; set; } = new Dictionary<string, string>();

        #endregion
    }
}
