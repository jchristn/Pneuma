namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request to approve a draft.</summary>
    public class OntologyApproveRequest
    {
        /// <summary>Optional change summary.</summary>
        public string? ChangeSummary { get; set; } = null;
    }
}
