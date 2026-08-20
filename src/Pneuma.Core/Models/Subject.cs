namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A subject archive owned by a tenant. Its knowledge graph is rooted at a LiteGraph node.
    /// </summary>
    public class Subject
    {
        #region Public-Members

        /// <summary>Subject identifier (prefix "sub_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Display name of the subject.</summary>
        public string DisplayName
        {
            get { return _DisplayName; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(DisplayName)); _DisplayName = value; }
        }

        /// <summary>Free-form kind of subject (e.g. "Person"); any value the operator chooses.</summary>
        public string Type { get; set; } = "Person";

        /// <summary>Optional biography / description.</summary>
        public string? Description { get; set; } = null;

        /// <summary>Identifier of the root LiteGraph node representing this subject.</summary>
        public string? GraphRootNodeId { get; set; } = null;

        /// <summary>
        /// URL-safe slug (unique within the tenant) used to address this subject in the user dashboard.
        /// Auto-generated from <see cref="DisplayName"/> when not supplied. Null until assigned.
        /// </summary>
        public string? UrlSlug { get; set; } = null;

        /// <summary>
        /// Whether model reasoning ("thinking") is rendered for chats about this subject. When false, thinking
        /// is still captured server-side but hidden in the chat UI and its statistics. Default false.
        /// </summary>
        public bool ThinkingEnabled { get; set; } = false;

        /// <summary>
        /// Optional subject-specific system prompt. Appended after the global system prompt for every chat
        /// about this subject (global base + subject appended). Null means global-only.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// Optional subject-specific ontology classification prompt. Appended after the global
        /// <c>ontology.classify</c> prompt during ingestion classification. Null means global-only.
        /// </summary>
        public string? OntologyClassifyPrompt { get; set; } = null;

        /// <summary>
        /// Optional subject-specific ontology definition. Appended after the global <c>ontology.definition</c>
        /// prompt when mapping atoms into the graph representation. Null means global-only.
        /// </summary>
        public string? OntologyDefinitionPrompt { get; set; } = null;

        /// <summary>
        /// Number of days chat-turn history is retained for this subject before pruning. Clamped to a minimum
        /// of 1. Default 90.
        /// </summary>
        public int HistoryRetentionDays
        {
            get { return _HistoryRetentionDays; }
            set { _HistoryRetentionDays = value < 1 ? 1 : value; }
        }

        /// <summary>Lifecycle state of this subject's tracked cascade deletion. Default <see cref="SubjectDeletionStatusEnum.None"/>.</summary>
        public SubjectDeletionStatusEnum DeletionStatus { get; set; } = SubjectDeletionStatusEnum.None;

        /// <summary>Whether the subject archive is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the subject is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSubjectId();
        private string _TenantId = String.Empty;
        private string _DisplayName = String.Empty;
        private int _HistoryRetentionDays = 90;

        #endregion
    }
}
