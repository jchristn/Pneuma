namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Search response: a representative set of graph nodes for a query.
    /// </summary>
    public class SearchResponse
    {
        /// <summary>The query that was executed.</summary>
        public string Query { get; set; } = string.Empty;

        /// <summary>Representative node results, most relevant first.</summary>
        public List<SearchNodeResult> Results { get; set; } = new List<SearchNodeResult>();
    }
}
