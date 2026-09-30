namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>A node type declared by an ontology version.</summary>
    public class OntologyNodeType
    {
        /// <summary>Type name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>What the type means.</summary>
        public string? Description { get; set; } = null;
    }
}
