namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A named, versioned prompt used for ingestion classification/summarization or user-query answering.
    /// </summary>
    public class Prompt
    {
        /// <summary>Prompt identifier (prefix "prm_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier. Null for global prompts.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Stable key identifying the prompt's role (e.g. "ontology.classify", "user.answer").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>The prompt content / template.</summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>Monotonic version number.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Whether the prompt is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the prompt is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
