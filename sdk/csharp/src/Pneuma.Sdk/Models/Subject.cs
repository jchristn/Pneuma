namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A subject archive owned by a tenant. Its knowledge graph is rooted at a graph node.
    /// </summary>
    public class Subject
    {
        /// <summary>Subject identifier (prefix "sub_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Display name of the subject.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>The kind of subject.</summary>
        public string Type { get; set; } = "Person";

        /// <summary>Optional biography / description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Identifier of the root graph node representing this subject.</summary>
        public string? GraphRootNodeId { get; set; } = null;

        /// <summary>Whether the subject archive is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the subject is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
