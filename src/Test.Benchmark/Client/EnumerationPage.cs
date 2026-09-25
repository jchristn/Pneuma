namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// Pneuma's paginated <c>EnumerationResult</c> envelope.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class EnumerationPage<T>
    {
        #region Public-Members

        /// <summary>
        /// Success flag.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Total records after filtering, before paging.
        /// </summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>
        /// True when no more pages follow.
        /// </summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>
        /// The page of records.
        /// </summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion
    }
}
