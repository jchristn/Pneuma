namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Enums;

    /// <summary>
    /// A model endpoint (model runner) surfaced to the dashboards. Backed by a Pneuma-native
    /// <see cref="Pneuma.Core.Models.ModelRunner"/> row; secrets are never returned.
    /// </summary>
    public class ModelEndpointDto
    {
        #region Public-Members

        /// <summary>Endpoint identifier (model runner id).</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Endpoint type: "Embedding" or "Completion".</summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>Provider family.</summary>
        public ModelRunnerProviderEnum Provider { get; set; } = ModelRunnerProviderEnum.Ollama;

        /// <summary>Human-readable name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Upstream provider URL.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>API format (e.g. "Ollama").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>
        /// Provider API key. Always null in responses — secrets are write-only and never returned.
        /// Retained for wire-shape compatibility with the dashboard/SDK.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>Azure OpenAI deployment name.</summary>
        public string? Deployment { get; set; } = null;

        /// <summary>Azure OpenAI API version.</summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>Cloud region (Bedrock/Vertex).</summary>
        public string? Region { get; set; } = null;

        /// <summary>Cloud project (Vertex).</summary>
        public string? Project { get; set; } = null;

        /// <summary>AWS access key id (Bedrock). Non-secret; the secret access key is never returned.</summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>Whether the endpoint is active.</summary>
        public bool Active { get; set; } = false;

        /// <summary>Maximum number of concurrent requests to send to this endpoint. Minimum 1. Default 2.</summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>Maximum number of requests that may queue for a slot once the concurrency limit is reached.</summary>
        public int MaxQueueDepth { get; set; } = 0;

        /// <summary>Completion model context window in tokens (0 = unset). Drives automatic chat compression.</summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Maximum upstream request timeout in milliseconds.</summary>
        public int MaximumTimeoutMs { get; set; } = 60000;

        /// <summary>Whether background health checks run against this endpoint.</summary>
        public bool HealthCheckEnabled { get; set; } = false;

        /// <summary>Health check probe URL.</summary>
        public string? HealthCheckUrl { get; set; } = null;

        /// <summary>Health check HTTP method: "GET" or "HEAD".</summary>
        public string? HealthCheckMethod { get; set; } = "GET";

        /// <summary>Milliseconds between health checks.</summary>
        public int HealthCheckIntervalMs { get; set; } = 0;

        /// <summary>Per-check HTTP timeout in milliseconds.</summary>
        public int HealthCheckTimeoutMs { get; set; } = 0;

        /// <summary>HTTP status code that indicates a healthy response.</summary>
        public int HealthCheckExpectedStatusCode { get; set; } = 200;

        /// <summary>Consecutive successes required to mark the endpoint healthy.</summary>
        public int HealthyThreshold { get; set; } = 2;

        /// <summary>Consecutive failures required to mark the endpoint unhealthy.</summary>
        public int UnhealthyThreshold { get; set; } = 2;

        /// <summary>Whether to send the endpoint's API key on health check requests.</summary>
        public bool HealthCheckUseAuth { get; set; } = false;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
