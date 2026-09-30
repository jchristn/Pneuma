namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Models;

    /// <summary>A request to replace a subject's starter questions. Each entry needs question; kind and origin are optional.</summary>
    public class SubjectQuestionsRequest
    {
        #region Public-Members

        /// <summary>The questions, in display order.</summary>
        public List<SubjectQuestion> Questions { get; set; } = new List<SubjectQuestion>();

        #endregion
    }
}
