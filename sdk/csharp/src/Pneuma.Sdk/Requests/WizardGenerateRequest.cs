namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>A request to draft one wizard step.</summary>
    public class WizardGenerateRequest
    {
        /// <summary>The draft so far.</summary>
        public SubjectWizardDraft Draft { get; set; } = new SubjectWizardDraft();

        /// <summary>Completion model endpoint; null uses the first active one.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>Optional guidance for the model.</summary>
        public string? Guidance { get; set; } = null;

        /// <summary>Questions step: replace (default) or more.</summary>
        public string? Mode { get; set; } = null;

        /// <summary>Questions step: how many to draft; 0 for the default.</summary>
        public int Count { get; set; } = 0;
    }
}
