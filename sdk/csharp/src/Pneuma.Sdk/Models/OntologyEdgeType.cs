namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>An edge type declared by an ontology version.</summary>
    public class OntologyEdgeType
    {
        /// <summary>Type name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What the type means.</summary>
        public string? Description { get; set; } = null;
    }
}
