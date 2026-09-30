namespace Pneuma.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>A request to create up to 100 evaluation facts.</summary>
    public class EvalFactBulkRequest
    {
        /// <summary>The facts (subjectId, question, expectedAnswer, optional category).</summary>
        public List<EvalFact> Facts { get; set; } = new List<EvalFact>();
    }
}
