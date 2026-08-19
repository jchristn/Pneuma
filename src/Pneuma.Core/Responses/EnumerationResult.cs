namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// A paginated enumeration result, following the platform enumeration pattern.
    /// </summary>
    /// <typeparam name="T">Record type.</typeparam>
    public class EnumerationResult<T>
    {
        #region Public-Members

        /// <summary>Whether the enumeration succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Maximum records requested for this page.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>Number of records skipped before this page.</summary>
        public int Skip { get; set; } = 0;

        /// <summary>Total records matching the query across all pages (after any search filter).</summary>
        public long TotalRecords { get; set; } = 0;

        /// <summary>Records remaining after this page.</summary>
        public long RecordsRemaining { get; set; } = 0;

        /// <summary>Whether this page reaches the end of the result set.</summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>The records on this page.</summary>
        public List<T> Objects { get; set; } = new List<T>();

        #endregion
    }
}
