namespace Pneuma.Core.Wizard
{
    using System;
    using System.Collections.Generic;

    /// <summary>The questions step's model output: the JSON shape the <c>wizard.questions.format</c> prompt asks for.</summary>
    public class WizardQuestionsOutput
    {
        #region Public-Members

        /// <summary>Proposed questions.</summary>
        public List<WizardQuestionOutput> Questions { get; set; } = new List<WizardQuestionOutput>();

        #endregion
    }
}
