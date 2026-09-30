namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>A relationship type in a wizard ontology draft, with the node types it connects.</summary>
    public class WizardEdgeType
    {
        #region Public-Members

        /// <summary>Type name (UPPER_SNAKE_CASE).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>What the relationship means, in one sentence.</summary>
        public string? Description { get; set; } = null;

        /// <summary>The node type it starts from, or null when any.</summary>
        public string? From { get; set; } = null;

        /// <summary>The node type it points to, or null when any.</summary>
        public string? To { get; set; } = null;

        /// <summary>The example questions this relationship helps answer, as 1-based positions in the draft's question list.</summary>
        public List<int> Questions { get; set; } = new List<int>();

        /// <summary>True when the user locked or edited it; regeneration keeps it verbatim.</summary>
        public bool Locked { get; set; } = false;

        #endregion
    }
}
