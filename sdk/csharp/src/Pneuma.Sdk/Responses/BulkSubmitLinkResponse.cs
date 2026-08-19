namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>
    /// Response reporting the result of a bulk link submission.
    /// </summary>
    public class BulkSubmitLinkResponse
    {
        /// <summary>Number of links created.</summary>
        public int Created { get; set; } = 0;

        /// <summary>The created links.</summary>
        public List<SubjectLink> Links { get; set; } = new List<SubjectLink>();
    }
}
