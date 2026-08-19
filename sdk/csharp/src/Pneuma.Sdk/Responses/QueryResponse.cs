namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>
    /// A grounded answer with its supporting sources.
    /// </summary>
    public class QueryResponse
    {
        /// <summary>The generated answer.</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>The graph nodes that grounded the answer.</summary>
        public List<GraphNode> Sources { get; set; } = new List<GraphNode>();

        /// <summary>Whether the corpus supported an answer.</summary>
        public bool Grounded { get; set; } = false;
    }
}
