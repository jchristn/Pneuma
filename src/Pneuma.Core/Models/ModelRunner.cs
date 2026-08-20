namespace Pneuma.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// An LLM/embedding endpoint Pneuma addresses through PolyPrompt for classification and answering.
    /// </summary>
    public class ModelRunner
    {
        #region Public-Members

        /// <summary>Model runner identifier (prefix "mr_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier. Null for global runners.</summary>
        public string? TenantId { get; set; } = null;

        /// <summary>Human-readable name.</summary>
        public string Name
        {
            get { return _Name; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name)); _Name = value; }
        }

        /// <summary>Provider family.</summary>
        public ModelRunnerProviderEnum Provider { get; set; } = ModelRunnerProviderEnum.Ollama;

        /// <summary>Base URL of the endpoint.</summary>
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>API type / format hint (provider-specific).</summary>
        public string? ApiType { get; set; } = null;

        /// <summary>Encrypted authentication material (API key), if required.</summary>
        public string? AuthMaterialEncrypted { get; set; } = null;

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

        /// <summary>
        /// Maximum context window (in tokens) of the model, when known. Drives automatic chat conversation
        /// compression once the running message history approaches the window. 0 means unknown/disabled.
        /// Not persisted for stored runners; populated on the transient runner resolved from a Partio endpoint.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Whether the runner is protected from deletion.</summary>
        public bool IsProtected { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC last-update timestamp.</summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateModelRunnerId();
        private string _Name = String.Empty;

        #endregion
    }
}
