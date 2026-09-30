namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;
    using Pneuma.Sdk.Models;

    /// <summary>The result of creating evaluation facts in bulk.</summary>
    public class EvalFactBulkResult
    {
        /// <summary>How many were created.</summary>
        public int Created { get; set; } = 0;

        /// <summary>The created facts.</summary>
        public List<EvalFact> Objects { get; set; } = new List<EvalFact>();
    }
}
