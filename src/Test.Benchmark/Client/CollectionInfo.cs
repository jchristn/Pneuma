namespace Test.Benchmark.Client
{
    /// <summary>
    /// A RecallDB collection as relayed by Pneuma.
    /// </summary>
    public class CollectionInfo
    {
        #region Public-Members

        /// <summary>
        /// Collection id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Vector dimensionality.
        /// </summary>
        public int Dimensionality { get; set; } = 0;

        #endregion
    }
}
