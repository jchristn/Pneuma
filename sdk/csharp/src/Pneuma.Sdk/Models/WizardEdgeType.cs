namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A relationship type in a wizard ontology draft.</summary>
    public class WizardEdgeType
    {
        /// <summary>Type name (UPPER_SNAKE_CASE).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Node type it starts from, or null for any.</summary>
        public string? From { get; set; } = null;

        /// <summary>Node type it points to, or null for any.</summary>
        public string? To { get; set; } = null;

        /// <summary>1-based numbers of the questions it serves.</summary>
        public List<int> Questions { get; set; } = new List<int>();

        /// <summary>True to keep it verbatim when the rest is regenerated.</summary>
        public bool Locked { get; set; } = false;
    }
}
