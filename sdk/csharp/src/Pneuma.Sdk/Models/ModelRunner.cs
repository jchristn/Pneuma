namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// An LLM/embedding endpoint Pneuma addresses for classification and answering. The API key is
    /// encrypted server-side and never returned.
    /// </summary>
    public class ModelRunner
    {
        /// <summary>Model runner identifier (prefix "mr_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier. Null for global runners.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Provider family.</summary>
        public ModelRunnerProviderEnum Provider { get; set; } = ModelRunnerProviderEnum.Ollama;

        /// <summary>Base URL of the endpoint.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>API type / format hint (provider-specific).</summary>
        public string? ApiType { get; set; } = null;

        /// <summary>Capabilities the runner exposes.</summary>
        public List<ModelCapabilityEnum> Capabilities { get; set; } = new List<ModelCapabilityEnum>();

        /// <summary>How the runner may be used within Pneuma.</summary>
        public ModelRunnerUsageEnum Usage { get; set; } = ModelRunnerUsageEnum.Both;

        /// <summary>Default model name for completions.</summary>
        public string? DefaultModel { get; set; } = null;

        /// <summary>Default model name for embeddings.</summary>
        public string? DefaultEmbeddingModel { get; set; } = null;

        /// <summary>Whether the runner is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>Whether the runner is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;
    }
}
