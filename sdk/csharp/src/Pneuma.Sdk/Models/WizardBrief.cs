namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>The subject brief the wizard drafts first.</summary>
    public class WizardBrief
    {
        /// <summary>Display name.</summary>
        public string? DisplayName { get; set; } = null;

        /// <summary>Free-text type.</summary>
        public string? Type { get; set; } = null;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Ask-page tagline.</summary>
        public string? Tagline { get; set; } = null;

        /// <summary>Audience.</summary>
        public string? Audience { get; set; } = null;

        /// <summary>Tone of answers.</summary>
        public string? Tone { get; set; } = null;
    }
}
