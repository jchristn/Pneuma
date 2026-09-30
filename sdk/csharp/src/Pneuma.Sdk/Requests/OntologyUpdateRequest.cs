namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request to rename or re-describe an ontology.</summary>
    public class OntologyUpdateRequest
    {
        /// <summary>New name, or null to keep it.</summary>
        public string? Name { get; set; } = null;

        /// <summary>New description.</summary>
        public string? Description { get; set; } = null;
    }
}
