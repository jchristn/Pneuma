namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Paginated envelope returned by list (GET-all) endpoints. Wraps a page of records together with
    /// paging metadata describing the total result set.
    /// </summary>
    /// <typeparam name="T">The type of record contained in the result set.</typeparam>
    public class EnumerationResult<T>
    {
        /// <summary>Indicates whether the enumeration succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Maximum number of records requested for this page.</summary>
        public int MaxResults { get; set; } = 100;

        /// <summary>Number of records skipped before this page.</summary>
        public int Skip { get; set; } = 0;

        /// <summary>Total number of records matching the query across all pages.</summary>
        public int TotalRecords { get; set; } = 0;

        /// <summary>Number of records remaining after this page.</summary>
        public int RecordsRemaining { get; set; } = 0;

        /// <summary>Indicates whether this page is the last page of results.</summary>
        public bool EndOfResults { get; set; } = true;

        /// <summary>The records on this page.</summary>
        public List<T> Objects { get; set; } = new List<T>();
    }
}
