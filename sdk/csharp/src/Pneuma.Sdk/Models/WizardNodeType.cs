namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A node type in a wizard ontology draft.</summary>
    public class WizardNodeType
    {
        /// <summary>Type name (PascalCase).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>1-based numbers of the questions it serves.</summary>
        public List<int> Questions { get; set; } = new List<int>();

        /// <summary>True to keep it verbatim when the rest is regenerated.</summary>
        public bool Locked { get; set; } = false;
    }
}
