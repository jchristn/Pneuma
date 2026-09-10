namespace Pneuma.Core.Requests
{
    /// <summary>
    /// Request to create or update a Partio model endpoint via the Pneuma model-runner proxy.
    /// </summary>
    public class CreateModelEndpointRequest
    {
        #region Public-Members

        /// <summary>Endpoint type: "Embedding" or "Completion".</summary>
        public string? Type { get; set; } = null;

        /// <summary>Human-readable name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Upstream provider URL (e.g. "http://ollama:11434").</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>API format (e.g. "Ollama", "OpenAI").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>Provider API key (write-only; forwarded to Partio, never returned).</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>Whether the endpoint is active.</summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Maximum number of concurrent requests Partio will send to this endpoint. Minimum 1 (Partio clamps).
        /// Default 2 to match Partio's canonical default.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Maximum number of requests that may wait for a concurrency slot once <see cref="MaxConcurrentRequests"/>
        /// upstream calls are in flight. Minimum 0 (Partio clamps). Default 0 — over-limit requests are rejected
        /// immediately rather than queued.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 0;

        /// <summary>
        /// Maximum context window (in tokens) of a completion model. Drives automatic chat conversation
        /// compression once the message history approaches the window. 0 disables compression.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Maximum upstream request timeout in milliseconds (Partio clamps to >= 1).</summary>
        public int MaximumTimeoutMs { get; set; } = 60000;

        /// <summary>Whether Partio runs background health checks against this endpoint.</summary>
        public bool HealthCheckEnabled { get; set; } = false;

        /// <summary>Health check probe URL. Blank lets Partio derive it from the endpoint + API format.</summary>
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
