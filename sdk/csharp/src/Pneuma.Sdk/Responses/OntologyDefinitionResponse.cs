namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>The definition text the classifier sees for a version.</summary>
    public class OntologyDefinitionResponse
    {
        /// <summary>Version identifier.</summary>
        public string VersionId { get; set; } = string.Empty;

        /// <summary>The rendered definition.</summary>
        public string Definition { get; set; } = string.Empty;
    }
}
