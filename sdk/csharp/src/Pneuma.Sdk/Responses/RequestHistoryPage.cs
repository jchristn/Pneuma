namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>
    /// A page of request history entries. List responses omit bodies to keep payloads small.
    /// </summary>
    public class RequestHistoryPage
    {
        /// <summary>The entries on this page.</summary>
        public List<RequestHistoryEntry> Items { get; set; } = new List<RequestHistoryEntry>();

        /// <summary>One-based page number.</summary>
        public int PageNumber { get; set; } = 1;

        /// <summary>Page size.</summary>
        public int PageSize { get; set; } = 25;

        /// <summary>Total number of matching entries.</summary>
        public long TotalCount { get; set; } = 0;
    }
}
