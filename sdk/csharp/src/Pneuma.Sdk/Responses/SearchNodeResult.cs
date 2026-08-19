namespace Pneuma.Sdk.Responses
{
    using Pneuma.Sdk.Models;

    /// <summary>
    /// A single search result: a representative graph node with its relevance score.
    /// </summary>
    public class SearchNodeResult
    {
        /// <summary>The resolved graph node.</summary>
        public GraphNode Node { get; set; } = new GraphNode();

        /// <summary>Relevance score from the search index.</summary>
        public double Score { get; set; } = 0;

        /// <summary>A text snippet, when available.</summary>
        public string? Snippet { get; set; } = null;
    }
}
