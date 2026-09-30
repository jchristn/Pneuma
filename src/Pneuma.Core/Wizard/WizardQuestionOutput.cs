namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>One proposed question as the model returns it.</summary>
    public class WizardQuestionOutput
    {
        #region Public-Members

        /// <summary>The question.</summary>
        public string? Question { get; set; } = null;

        /// <summary>The kind, as text (fact, relationship, timeline, comparison, reasoning, or overview).</summary>
        public string? Kind { get; set; } = null;

        #endregion
    }
}
