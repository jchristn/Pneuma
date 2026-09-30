namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to start a new draft version of an ontology.</summary>
    public class OntologyDraftRequest
    {
        #region Public-Members

        /// <summary>The version to copy, or null to copy the ontology's newest version.</summary>
        public string? BasedOnVersionId { get; set; } = null;

        #endregion
    }
}
