namespace Pneuma.Core.Wizard
{
    using System;

    /// <summary>A wizard prompt's key, display name, and default content (seeded as a system prompt).</summary>
    public class SubjectWizardPromptDefault
    {
        #region Public-Members

        /// <summary>Prompt key.</summary>
        public string Key { get; set; } = String.Empty;

        /// <summary>Display name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Default content.</summary>
        public string Content { get; set; } = String.Empty;

        #endregion
    }
}
