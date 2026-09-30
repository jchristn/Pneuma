namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>One finding of an ontology operation.</summary>
    public class OntologyOperationItem
    {
        /// <summary>Operation identifier.</summary>
        public string OperationId { get; set; } = string.Empty;

        /// <summary>Position.</summary>
        public int Ordinal { get; set; } = 0;

        /// <summary>Graph node.</summary>
        public string? NodeId { get; set; } = null;

        /// <summary>Text excerpt.</summary>
        public string? Excerpt { get; set; } = null;

        /// <summary>Whether it is a finding.</summary>
        public bool Changed { get; set; } = false;

        /// <summary>What was found.</summary>
        public string? Detail { get; set; } = null;
    }
}
