namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Models;

    /// <summary>A request to create several evaluation facts at once (up to 100).</summary>
    public class EvalFactBulkRequest
    {
        #region Public-Members

        /// <summary>Most facts one request may create.</summary>
        public const int MaxFacts = 100;

        /// <summary>The facts: subjectId, question, expectedAnswer, and an optional category each.</summary>
        public List<EvalFact> Facts { get; set; } = new List<EvalFact>();

        #endregion
    }
}
