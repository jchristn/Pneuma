namespace Pneuma.Core.Models
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;
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
        /// embedding endpoint id used to vectorize this subject's content at ingestion and to embed
        /// queries when answering about it. Required before links can be ingested or questions answered.
        /// </summary>
        public string? EmbeddingModel { get; set; } = null;

        /// <summary>
        /// completion endpoint id used for this subject's inference (ingestion classification/
        /// summarization and answer generation). Required before links can be ingested or questions answered.
        /// </summary>
        public string? InferenceModel { get; set; } = null;

        /// <summary>
        /// Optional completion endpoint id used to re-rank retrieved passages by relevance before
        /// answering. Null disables the reranking step.
        /// </summary>
        public string? RerankingModel { get; set; } = null;

        /// <summary>
        /// Which reranking strategy this subject uses: LLM listwise (via <see cref="RerankingModel"/>, the
        /// default) or a dedicated cross-encoder rerank endpoint (configured globally). When set to
        /// cross-encoder but none is configured, reranking falls back to the LLM listwise path.
        /// </summary>
        public RerankerTypeEnum RerankerType { get; set; } = RerankerTypeEnum.LlmListwise;

        /// <summary>
        /// Optional completion endpoint id used to rewrite the user's question into a retrieval query
        /// before searching. Null disables the prompt-rewrite step.
        /// </summary>
        public string? PromptRewriteModel { get; set; } = null;

        /// <summary>
        /// RecallDB collection id where this subject's ingested chunks are stored and searched. Its
        /// dimensionality must match <see cref="EmbeddingModel"/>. Required before links can be ingested.
        /// </summary>
        public string? Collection { get; set; } = null;

        /// <summary>
        /// Chunking strategy for this subject's ingested content (e.g. "FixedTokenCount"). Null uses the
        /// platform default. Lets short-form and long-form subjects chunk differently.
        /// </summary>
        public string? ChunkStrategy { get; set; } = null;

        /// <summary>Target chunk size in tokens for this subject's ingestion. Clamped to [16, 8192]. Default 256.</summary>
        public int ChunkMaxTokens
        {
            get { return _ChunkMaxTokens; }
            set { _ChunkMaxTokens = Math.Clamp(value, 16, 8192); }
        }

        /// <summary>Overlap between adjacent chunks in tokens for this subject's ingestion. Clamped to [0, 4096]. Default 32.</summary>
        public int ChunkOverlapTokens
        {
            get { return _ChunkOverlapTokens; }
            set { _ChunkOverlapTokens = Math.Clamp(value, 0, 4096); }
        }

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
        /// Optional default retrieval facet filter for this subject, serialized as a <see cref="Requests.RetrievalFilter"/>
        /// JSON payload (schemaless column). Applied to every query about this subject; a per-request filter is
        /// merged with it (union of required and excluded). Null applies no default filter.
        /// </summary>
        public string? RetrievalFilterJson { get; set; } = null;

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

        /// <summary>
        /// Whether this subject is published to the end-user (consumer) chat experience. When false, the subject
        /// is hidden from the consumer dashboard's subject list and its ask page is unavailable, so an operator
        /// can review a freshly-built archive before exposing it. Operator dashboards always see it regardless.
        /// Default true, so existing subjects remain visible without any action.
        /// </summary>
        public bool PublishedForChat { get; set; } = true;

        /// <summary>
        /// The subject's ingestion concurrency/tuning overrides, serialized as JSON, or null when the subject
        /// inherits the system defaults for everything. Persisted form; the API surface uses
        /// <see cref="ConcurrencyOverrides"/>.
        /// </summary>
        [JsonIgnore]
        public string? ConcurrencyOverridesJson { get; set; } = null;

        /// <summary>The subject's ingestion concurrency overrides (null to inherit the system defaults for everything).</summary>
        public SubjectConcurrencyOverrides? ConcurrencyOverrides
        {
            get { return GetConcurrencyOverrides(); }
            set { SetConcurrencyOverrides(value); }
        }

        /// <summary>Whether the subject is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Public-Methods

        /// <summary>Deserialize the subject's concurrency overrides, or null when none are set / the JSON is invalid.</summary>
        /// <returns>The overrides, or null.</returns>
        public SubjectConcurrencyOverrides? GetConcurrencyOverrides()
        {
            if (String.IsNullOrWhiteSpace(ConcurrencyOverridesJson)) return null;
            try { return JsonSerializer.Deserialize<SubjectConcurrencyOverrides>(ConcurrencyOverridesJson!); }
            catch (JsonException) { return null; }
        }

        /// <summary>Set the subject's concurrency overrides; a null or empty set clears the stored JSON.</summary>
        /// <param name="overrides">The overrides to store, or null to clear.</param>
        public void SetConcurrencyOverrides(SubjectConcurrencyOverrides? overrides)
        {
            if (overrides == null || overrides.IsEmpty()) { ConcurrencyOverridesJson = null; return; }
            ConcurrencyOverridesJson = JsonSerializer.Serialize(overrides);
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSubjectId();
        private string _TenantId = String.Empty;
        private string _DisplayName = String.Empty;
        private int _HistoryRetentionDays = 90;
        private int _ChunkMaxTokens = 256;
        private int _ChunkOverlapTokens = 32;

        #endregion
    }
}
