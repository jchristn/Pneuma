namespace Test.Benchmark.Datasets
{
    using System.Collections.Generic;

    /// <summary>
    /// A metadata filter in Pneuma's <c>RetrievalFilter</c> shape (labels and tag conditions).
    /// </summary>
    public class QueryFilter
    {
        #region Public-Members

        /// <summary>
        /// Labels every matching chunk must carry.
        /// </summary>
        public List<string>? RequiredLabels { get; set; } = null;

        /// <summary>
        /// Labels that exclude a chunk.
        /// </summary>
        public List<string>? ExcludedLabels { get; set; } = null;

        /// <summary>
        /// Tag conditions every matching chunk must satisfy.
        /// </summary>
        public List<TagCondition>? RequiredTags { get; set; } = null;

        /// <summary>
        /// Tag conditions that exclude a chunk.
        /// </summary>
        public List<TagCondition>? ExcludedTags { get; set; } = null;

        #endregion
    }
}
