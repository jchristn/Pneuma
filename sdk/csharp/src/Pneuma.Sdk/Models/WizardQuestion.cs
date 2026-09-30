namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>An example question in a wizard draft.</summary>
    public class WizardQuestion
    {
        /// <summary>The question.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>Kind of question.</summary>
        public SubjectQuestionKindEnum Kind { get; set; } = SubjectQuestionKindEnum.Fact;

        /// <summary>Who wrote it (an edited model question counts as the user's).</summary>
        public SubjectQuestionOriginEnum Origin { get; set; } = SubjectQuestionOriginEnum.Model;

        /// <summary>True to keep it verbatim when the rest is regenerated.</summary>
        public bool Locked { get; set; } = false;
    }
}
