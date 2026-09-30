namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The sources step's model output: the JSON shape the <c>wizard.sources.format</c> prompt asks for.</summary>
    public class WizardSourcesOutput
    {
        #region Public-Members

        /// <summary>Suggestions.</summary>
        public List<WizardSourceSuggestion> Suggestions { get; set; } = new List<WizardSourceSuggestion>();

        #endregion
    }
}
