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

        /// <summary>Azure OpenAI deployment name. Required when <see cref="Provider"/> is AzureOpenAI.</summary>
        public string? Deployment { get; set; } = null;

        /// <summary>Azure OpenAI API version. Optional; a provider default applies when null.</summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>Cloud region. Required when <see cref="Provider"/> is Bedrock or VertexAI.</summary>
        public string? Region { get; set; } = null;

        /// <summary>Cloud project identifier. Required when <see cref="Provider"/> is VertexAI.</summary>
        public string? Project { get; set; } = null;

        /// <summary>AWS access key id. Required when <see cref="Provider"/> is Bedrock. The secret access key is stored in <see cref="AuthMaterialEncrypted"/>.</summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>Encrypted AWS session token, when using temporary credentials with Bedrock.</summary>
        public string? SessionTokenEncrypted { get; set; } = null;

        /// <summary>Whether the runner is enabled.</summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Maximum context window (in tokens) of the model, when known. Drives automatic chat conversation
        /// compression once the running message history approaches the window. 0 means unknown/disabled.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Maximum number of concurrent requests sent to this endpoint. Minimum 1.</summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>Maximum number of requests that may queue for a concurrency slot. Minimum 0.</summary>
        public int MaxQueueDepth { get; set; } = 0;

        /// <summary>Maximum upstream request timeout in milliseconds. Minimum 1.</summary>
        public int MaximumTimeoutMs { get; set; } = 60000;

        /// <summary>Whether background health checks run against this endpoint. Enabled by default so endpoints are monitored unless the operator opts out.</summary>
        public bool HealthCheckEnabled { get; set; } = true;

        /// <summary>Health check probe URL. Null/blank derives it from <see cref="BaseUrl"/>.</summary>
        public string? HealthCheckUrl { get; set; } = null;

        /// <summary>Health check HTTP method (e.g. "GET" or "HEAD").</summary>
        public string? HealthCheckMethod { get; set; } = "GET";

        /// <summary>Milliseconds between health checks. 0 uses the monitor default.</summary>
        public int HealthCheckIntervalMs { get; set; } = 0;

        /// <summary>Per-check HTTP timeout in milliseconds. 0 uses the monitor default.</summary>
        public int HealthCheckTimeoutMs { get; set; } = 0;

        /// <summary>HTTP status code that indicates a healthy response.</summary>
        public int HealthCheckExpectedStatusCode { get; set; } = 200;

        /// <summary>Consecutive successes required to mark the endpoint healthy. Minimum 1.</summary>
        public int HealthyThreshold { get; set; } = 2;

        /// <summary>Consecutive failures required to mark the endpoint unhealthy. Minimum 1.</summary>
        public int UnhealthyThreshold { get; set; } = 2;

        /// <summary>Whether to send the endpoint's API key as a bearer token on health check requests.</summary>
        public bool HealthCheckUseAuth { get; set; } = false;

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
