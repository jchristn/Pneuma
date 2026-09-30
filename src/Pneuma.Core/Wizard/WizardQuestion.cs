namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>An example question in a wizard draft.</summary>
    public class WizardQuestion
    {
        #region Public-Members

        /// <summary>The question.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>The kind of question.</summary>
        public SubjectQuestionKindEnum Kind { get; set; } = SubjectQuestionKindEnum.Fact;

        /// <summary>Whether a model or a person wrote it (an edited model question counts as the person's).</summary>
        public SubjectQuestionOriginEnum Origin { get; set; } = SubjectQuestionOriginEnum.Model;

        /// <summary>True when the user locked it; regeneration keeps it verbatim.</summary>
        public bool Locked { get; set; } = false;

        #endregion
    }
}
