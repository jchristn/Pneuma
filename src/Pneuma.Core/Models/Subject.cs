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

        /// <summary>
        /// Default <see cref="Tagline"/> text, applied at creation when none is supplied. Mirrors the
        /// user dashboard's built-in ask-page subtitle so a subject reads sensibly out of the box.
        /// </summary>
        public const string DefaultTagline = "Get an answer grounded in the archive, with the sources that support it.";

        /// <summary>
        /// Subject-configurable subtitle shown beneath the subject's name on its ask page in the user
        /// dashboard (under the search hero before asking, and under the chat header after). Falls back to
        /// the dashboard's built-in label when empty. Defaults to <see cref="DefaultTagline"/> at creation.
        /// </summary>
        public string? Tagline { get; set; } = null;

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
        /// Partio embedding endpoint id used to vectorize this subject's content at ingestion and to embed
        /// queries when answering about it. Required before links can be ingested or questions answered.
        /// </summary>
        public string? EmbeddingModel { get; set; } = null;

        /// <summary>
        /// Partio completion endpoint id used for this subject's inference (ingestion classification/
        /// summarization and answer generation). Required before links can be ingested or questions answered.
        /// </summary>
        public string? InferenceModel { get; set; } = null;

        /// <summary>
        /// Optional Partio completion endpoint id used to re-rank retrieved passages by relevance before
        /// answering. Null disables the reranking step.
        /// </summary>
        public string? RerankingModel { get; set; } = null;

        /// <summary>
        /// Optional Partio completion endpoint id used to rewrite the user's question into a retrieval query
        /// before searching. Null disables the prompt-rewrite step.
        /// </summary>
        public string? PromptRewriteModel { get; set; } = null;

        /// <summary>
        /// RecallDB collection id where this subject's ingested chunks are stored and searched. Its
        /// dimensionality must match <see cref="EmbeddingModel"/>. Required before links can be ingested.
        /// </summary>
        public string? Collection { get; set; } = null;

        /// <summary>Default <see cref="RerankingPrompt"/>, applied at creation when none is supplied.</summary>
        public const string DefaultRerankingPrompt = "Rank the candidate passages by how well they help answer the question. Consider only relevance, not length or writing style.";

        /// <summary>
        /// Optional subject-specific reranking prompt. Appended after the global <c>reranking</c> prompt when a
        /// reranking model is configured. Defaults to <see cref="DefaultRerankingPrompt"/> at creation.
        /// </summary>
        public string? RerankingPrompt { get; set; } = null;

        /// <summary>Default <see cref="PromptRewritePrompt"/>, applied at creation when none is supplied.</summary>
        public const string DefaultPromptRewritePrompt = "Rewrite the question into a single, self-contained search query for this subject's archive: resolve references, expand abbreviations, and keep it concise.";

        /// <summary>
        /// Optional subject-specific prompt-rewrite prompt. Appended after the global <c>prompt.rewrite</c>
        /// prompt when a prompt-rewrite model is configured. Defaults to <see cref="DefaultPromptRewritePrompt"/>.
        /// </summary>
        public string? PromptRewritePrompt { get; set; } = null;

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
