namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>A tenant's governed ontology.</summary>
    public class Ontology
    {
        /// <summary>Ontology identifier (prefix "ont_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Name, unique within the tenant.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
