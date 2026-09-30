namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>One proposed node or relationship type as the model returns it.</summary>
    public class WizardTypeOutput
    {
        #region Public-Members

        /// <summary>Type name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Relationship types only: the node type it starts from.</summary>
        public string? From { get; set; } = null;

        /// <summary>Relationship types only: the node type it points to.</summary>
        public string? To { get; set; } = null;

        /// <summary>1-based positions of the questions it serves.</summary>
        public List<int> Questions { get; set; } = new List<int>();

        #endregion
    }
}
