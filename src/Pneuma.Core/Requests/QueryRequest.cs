namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>
    /// A grounded question against a subject's corpus.
    /// </summary>
    public class QueryRequest
    {
        /// <summary>The natural-language question.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>Maximum sources to retrieve.</summary>
        public int MaxResults { get; set; } = 8;
    }
}
