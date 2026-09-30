namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to approve a draft ontology version.</summary>
    public class OntologyApproveRequest
    {
        #region Public-Members

        /// <summary>Optional summary of what changed; replaces the draft's change summary when set.</summary>
        public string? ChangeSummary { get; set; } = null;

        #endregion
    }
}
