namespace Pneuma.Core.Requests
{
    using Pneuma.Core.Enums;
    using Pneuma.Core.Ingestion.Enums;

    /// <summary>
    /// Request to create or update a model endpoint (model runner).
    /// </summary>
    public class CreateModelEndpointRequest
    {
        #region Public-Members

        /// <summary>Endpoint type: "Embedding" or "Completion".</summary>
        public string? Type { get; set; } = null;

        /// <summary>Provider family. When null, the provider is inferred from <see cref="ApiFormat"/>.</summary>
        public ModelRunnerProviderEnum? Provider { get; set; } = null;

        /// <summary>Human-readable name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Upstream provider URL (e.g. "http://127.0.0.1:11434").</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>API format (e.g. "Ollama", "OpenAI").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>Provider API key (also the Azure api-key / Vertex bearer token). Used as the primary secret for every provider except Bedrock, which uses <see cref="SecretAccessKey"/>. Write-only.</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>AWS secret access key (required for Bedrock). Stored as the endpoint's primary secret. Write-only; returned only on a single-endpoint read for viewing.</summary>
        public string? SecretAccessKey { get; set; } = null;

        /// <summary>Azure OpenAI deployment name (required for AzureOpenAI).</summary>
        public string? Deployment { get; set; } = null;

        /// <summary>Azure OpenAI API version.</summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>Cloud region (required for Bedrock and Vertex).</summary>
        public string? Region { get; set; } = null;

        /// <summary>Cloud project (required for Vertex).</summary>
        public string? Project { get; set; } = null;

        /// <summary>AWS access key id (required for Bedrock).</summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>AWS session token (Bedrock temporary credentials). Write-only; never returned.</summary>
        public string? SessionToken { get; set; } = null;

        /// <summary>Whether the endpoint is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent requests sent to this endpoint. Minimum 1 (clamped).
        /// Default 2.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Maximum number of requests that may wait for a concurrency slot once <see cref="MaxConcurrentRequests"/>
        /// upstream calls are in flight. Minimum 0 (clamped). Default 0 — over-limit requests are rejected
        /// immediately rather than queued.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 0;

        /// <summary>
        /// Maximum context window (in tokens) of a completion model. Drives automatic chat conversation
        /// compression once the message history approaches the window. 0 disables compression.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Maximum upstream request timeout in milliseconds (clamped to >= 1).</summary>
        public int MaximumTimeoutMs { get; set; } = 60000;

        /// <summary>Whether background health checks run against this endpoint.</summary>
        public bool HealthCheckEnabled { get; set; } = false;

        /// <summary>Health check probe URL. Blank derives it from the endpoint + API format.</summary>
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

        #endregion
    }
}
