namespace Pneuma.Sdk.Requests
{
    using System;

    /// <summary>Request to create an ontology.</summary>
    public class OntologyCreateRequest
    {
        /// <summary>Name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Template to start from (for example "Default").</summary>
        public string? Template { get; set; } = null;

        /// <summary>Version to copy.</summary>
        public string? CopyFromVersionId { get; set; } = null;
    }
}
