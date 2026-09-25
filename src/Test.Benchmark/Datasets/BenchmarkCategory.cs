namespace Test.Benchmark.Datasets
{
    /// <summary>
    /// A document category. Pneuma has no categories, so a document's category is applied as a link label.
    /// </summary>
    public class BenchmarkCategory
    {
        #region Public-Members

        /// <summary>
        /// Category name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Category description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        #endregion
    }
}
