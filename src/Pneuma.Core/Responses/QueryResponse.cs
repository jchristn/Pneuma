namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Graph;
    using Pneuma.Core.Ingestion.Graph;

    /// <summary>
    /// A grounded answer with its supporting sources.
    /// </summary>
    public class QueryResponse
    {
        /// <summary>The generated answer.</summary>
        public string Answer { get; set; } = String.Empty;

        /// <summary>The graph nodes that grounded the answer.</summary>
        public List<GraphNode> Sources { get; set; } = new List<GraphNode>();

        /// <summary>Whether the corpus supported an answer.</summary>
        public bool Grounded { get; set; } = false;

        /// <summary>The model that produced the answer, or null when no model ran.</summary>
        public string? Model { get; set; } = null;

        /// <summary>The wall-clock answer-generation time in milliseconds, or null when no model ran.</summary>
        public long? GenerationMs { get; set; } = null;

        /// <summary>
        /// True when the answer is a refusal because retrieval found no supporting passages (the answer text is
        /// the fixed "not enough information" message). A model may still decline in its own words while this
        /// stays false.
        /// </summary>
        public bool InsufficientSupport { get; set; } = false;
    }
}
