namespace Test.Benchmark.Client
{
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Body for <c>POST /v1.0/query</c> (and <c>/v1.0/query/global</c>).
    /// </summary>
    public class QueryBody
    {
        #region Public-Members

        /// <summary>
        /// The question.
        /// </summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>
        /// Maximum sources (null = server default).
        /// </summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>
        /// Subject scope.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Optional metadata filter.
        /// </summary>
        public QueryFilter? MetadataFilter { get; set; } = null;

        #endregion
    }
}
