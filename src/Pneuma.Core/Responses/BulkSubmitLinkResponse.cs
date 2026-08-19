namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;
    using Pneuma.Core.Models;

    /// <summary>
    /// The result of a bulk link submission: the number of links created and the created links.
    /// </summary>
    public class BulkSubmitLinkResponse
    {
        /// <summary>The number of links created.</summary>
        public int Created { get; set; } = 0;

        /// <summary>The created links.</summary>
        public List<SubjectLink> Links { get; set; } = new List<SubjectLink>();
    }
}
