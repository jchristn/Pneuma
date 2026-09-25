namespace Test.Benchmark.Datasets
{
    /// <summary>
    /// One tag condition of a <see cref="QueryFilter"/>.
    /// </summary>
    public class TagCondition
    {
        #region Public-Members

        /// <summary>
        /// Tag key.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Condition name (Equals, NotEquals, Contains, StartsWith, EndsWith, GreaterThan, LessThan, IsNull, IsNotNull).
        /// </summary>
        public string Condition { get; set; } = "Equals";

        /// <summary>
        /// Comparison value.
        /// </summary>
        public string? Value { get; set; } = null;

        #endregion
    }
}
