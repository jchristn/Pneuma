namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request to pin (or unpin) a subject's ontology version.</summary>
    public class SubjectOntologyRequest
    {
        /// <summary>Approved version to pin, or null to unpin.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>Queue a retag when the taxonomy changes.</summary>
        public bool Retag { get; set; } = true;
    }
}
