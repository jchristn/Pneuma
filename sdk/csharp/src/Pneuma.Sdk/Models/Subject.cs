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

        /// <summary>URL-safe slug (unique within the tenant) used to address this subject. Auto-generated from
        /// the display name when omitted.</summary>
        public string? UrlSlug { get; set; } = null;

        /// <summary>Whether model reasoning ("thinking") is rendered for chats about this subject.</summary>
        public bool ThinkingEnabled { get; set; } = false;

        /// <summary>Subject-specific system prompt, appended after the global system prompt for its chats.</summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>Subject-specific ontology classification prompt, appended after the global one during ingestion.</summary>
        public string? OntologyClassifyPrompt { get; set; } = null;

        /// <summary>Subject-specific ontology definition, appended after the global one during ingestion.</summary>
        public string? OntologyDefinitionPrompt { get; set; } = null;

        /// <summary>Number of days chat-turn history is retained for this subject (minimum 1). Default 90.</summary>
        public int HistoryRetentionDays { get; set; } = 90;

        /// <summary>Lifecycle state of this subject's tracked cascade deletion: None, Pending, Deleting, or Failed.</summary>
        public string DeletionStatus { get; set; } = "None";

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
