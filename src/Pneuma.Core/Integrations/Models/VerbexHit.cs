namespace Pneuma.Core.Integrations.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A Verbex search hit. Metadata (including the LiteGraph node id) round-trips on the hit.
    /// </summary>
    public class VerbexHit
    {
        /// <summary>Verbex document identifier.</summary>
        public string DocumentId { get; set; } = String.Empty;

        /// <summary>Relevance score.</summary>
        public double Score { get; set; } = 0;

        /// <summary>Document tags (includes litegraphNodeId).</summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();

        /// <summary>A text snippet, when available.</summary>
        public string? Snippet { get; set; } = null;
    }
}
