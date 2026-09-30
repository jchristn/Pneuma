namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>A request to draft one wizard step.</summary>
    public class WizardGenerateRequest
    {
        #region Public-Members

        /// <summary>The draft so far.</summary>
        public SubjectWizardDraft Draft
        {
            get { return _Draft; }
            set { _Draft = value ?? new SubjectWizardDraft(); }
        }

        /// <summary>The completion model endpoint to draft with; null uses the tenant's first active completion endpoint.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>Optional guidance for this generation, such as "more about his early career".</summary>
        public string? Guidance { get; set; } = null;

        /// <summary>Questions step only: "replace" (default) replaces unlocked questions; "more" adds new ones.</summary>
        public string? Mode { get; set; } = null;

        /// <summary>Questions step only: how many questions to draft; 0 uses the default.</summary>
        public int Count
        {
            get { return _Count; }
            set { _Count = Math.Max(0, value); }
        }

        #endregion

        #region Private-Members

        private SubjectWizardDraft _Draft = new SubjectWizardDraft();
        private int _Count = 0;

        #endregion
    }
}
