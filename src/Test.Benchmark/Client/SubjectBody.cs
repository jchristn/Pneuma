namespace Test.Benchmark.Client
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Test.Benchmark.Models;

    /// <summary>
    /// A Pneuma subject, used both to create/update subjects and to read them back. Pneuma's update route replaces
    /// every field it knows, so this type declares all of them and an update is always read-modify-write of the
    /// whole record. Null fields are omitted on write so server defaults apply on create.
    /// </summary>
    public class SubjectBody
    {
        #region Public-Members

        /// <summary>
        /// Tagline.
        /// </summary>
        public string? Tagline { get; set; } = null;

        /// <summary>
        /// URL slug.
        /// </summary>
        public string? UrlSlug { get; set; } = null;

        /// <summary>
        /// Active flag.
        /// </summary>
        public bool? Active { get; set; } = null;

        /// <summary>
        /// Visible in consumer chat.
        /// </summary>
        public bool? PublishedForChat { get; set; } = null;

        /// <summary>
        /// Show model reasoning.
        /// </summary>
        public bool? ThinkingEnabled { get; set; } = null;

        /// <summary>
        /// Subject system prompt addendum.
        /// </summary>
        public string? SystemPrompt { get; set; } = null;

        /// <summary>
        /// Ontology classification prompt addendum.
        /// </summary>
        public string? OntologyClassifyPrompt { get; set; } = null;

        /// <summary>
        /// Ontology definition prompt addendum.
        /// </summary>
        public string? OntologyDefinitionPrompt { get; set; } = null;

        /// <summary>
        /// Reranking prompt addendum.
        /// </summary>
        public string? RerankingPrompt { get; set; } = null;

        /// <summary>
        /// Prompt-rewrite prompt addendum.
        /// </summary>
        public string? PromptRewritePrompt { get; set; } = null;

        /// <summary>
        /// Default retrieval filter (serialized).
        /// </summary>
        public string? RetrievalFilterJson { get; set; } = null;

        /// <summary>
        /// Concurrency overrides (serialized).
        /// </summary>
        public string? ConcurrencyOverridesJson { get; set; } = null;

        /// <summary>
        /// Chat-history retention.
        /// </summary>
        public int? HistoryRetentionDays { get; set; } = null;

        /// <summary>
        /// Deletion status (read only; None unless a delete is in progress).
        /// </summary>
        public string? DeletionStatus { get; set; } = null;

        /// <summary>
        /// Subject id (read only).
        /// </summary>
        public string? Id { get; set; } = null;

        /// <summary>
        /// Display name (the deterministic bench subject name).
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Subject type.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Description (the harness records the configuration here).
        /// </summary>
        public string? Description { get; set; } = null;

        /// <summary>
        /// Embedding model endpoint id.
        /// </summary>
        public string? EmbeddingModel { get; set; } = null;

        /// <summary>
        /// Completion model endpoint id (classification, summarization, answers).
        /// </summary>
        public string? InferenceModel { get; set; } = null;

        /// <summary>
        /// Optional reranking model endpoint id.
        /// </summary>
        public string? RerankingModel { get; set; } = null;

        /// <summary>
        /// LlmListwise or CrossEncoder (older builds send and expect 0 or 1).
        /// </summary>
        [JsonConverter(typeof(FlexibleEnumConverter))]
        public string? RerankerType { get; set; } = null;

        /// <summary>
        /// Optional prompt-rewrite model endpoint id.
        /// </summary>
        public string? PromptRewriteModel { get; set; } = null;

        /// <summary>
        /// RecallDB collection id.
        /// </summary>
        public string? Collection { get; set; } = null;

        /// <summary>
        /// Chunking strategy.
        /// </summary>
        public string? ChunkStrategy { get; set; } = null;

        /// <summary>
        /// Chunk size in tokens (0 = server default when written).
        /// </summary>
        public int? ChunkMaxTokens { get; set; } = null;

        /// <summary>
        /// Chunk overlap in tokens.
        /// </summary>
        public int? ChunkOverlapTokens { get; set; } = null;

        /// <summary>
        /// Per-subject ingestion concurrency overrides.
        /// </summary>
        public Dictionary<string, int?>? ConcurrencyOverrides { get; set; } = null;

        #endregion
    }
}
