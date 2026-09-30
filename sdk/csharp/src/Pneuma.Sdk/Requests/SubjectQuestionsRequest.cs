namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>A request to replace a subject's starter questions.</summary>
    public class SubjectQuestionsRequest
    {
        /// <summary>The questions, in order (question and kind are used).</summary>
        public List<SubjectQuestion> Questions { get; set; } = new List<SubjectQuestion>();
    }
}
