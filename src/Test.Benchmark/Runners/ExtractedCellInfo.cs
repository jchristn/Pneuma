namespace Test.Benchmark.Runners
{
    /// <summary>
    /// One extracted cell from a link's atoms artifact.
    /// </summary>
    public class ExtractedCellInfo
    {
        #region Public-Members

        /// <summary>
        /// Cell type.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Cell text.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Cell title, when any.
        /// </summary>
        public string? Title { get; set; } = null;

        #endregion
    }
}
