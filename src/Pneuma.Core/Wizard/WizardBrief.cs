namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The subject brief the wizard drafts first: what the subject is, who asks about it, and how answers should sound.</summary>
    public class WizardBrief
    {
        #region Public-Members

        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>Free-text type, such as "Musician" or "Medical device company".</summary>
        public string? Type { get; set; } = null;

        /// <summary>One-paragraph description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Ask-page tagline.</summary>
        public string? Tagline { get; set; } = null;

        /// <summary>Who will ask about the subject.</summary>
        public string? Audience { get; set; } = null;

        /// <summary>The tone answers should take.</summary>
        public string? Tone { get; set; } = null;

        #endregion
    }
}
