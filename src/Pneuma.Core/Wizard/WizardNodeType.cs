namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>A node type in a wizard ontology draft.</summary>
    public class WizardNodeType
    {
        #region Public-Members

        /// <summary>Type name (PascalCase).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>What the type means, in one sentence.</summary>
        public string? Description { get; set; } = null;

        /// <summary>The example questions this type helps answer, as 1-based positions in the draft's question list.</summary>
        public List<int> Questions { get; set; } = new List<int>();

        /// <summary>True when the user locked or edited it; regeneration keeps it verbatim.</summary>
        public bool Locked { get; set; } = false;

        #endregion
    }
}
