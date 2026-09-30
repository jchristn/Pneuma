namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to pin a subject to an approved ontology version (or unpin it).</summary>
    public class SubjectOntologyRequest
    {
        #region Public-Members

        /// <summary>The approved version to pin, or null to unpin (the subject goes back to the ontology.definition prompt).</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>Queue a retag operation when the taxonomy changes, so existing cells are linked to the new concepts. Default true.</summary>
        public bool Retag { get; set; } = true;

        #endregion
    }
}
