namespace Pneuma.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A Partio embedding or completion endpoint, as exposed by the endpoint enumerate APIs.
    /// </summary>
    public class PartioEndpoint
    {
        /// <summary>Endpoint identifier (e.g. "default").</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Human-readable endpoint name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>API format (e.g. "Ollama").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>Upstream provider URL (e.g. "http://ollama:11434").</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>Provider API key (write-only; null on reads).</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>Whether the endpoint is active.</summary>
        public bool Active { get; set; } = false;

        /// <summary>
        /// Maximum number of concurrent requests Partio will send to this endpoint. Paired with Partio's
        /// endpoint <c>MaxConcurrentRequests</c> property; minimum 1 (Partio clamps). Default 2 to match
        /// Partio's own canonical default so a create followed by a read does not flip the value.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Maximum number of requests that may wait for a concurrency slot once <see cref="MaxConcurrentRequests"/>
        /// upstream calls are in flight. Paired with Partio's endpoint <c>MaxQueueDepth</c> property; minimum 0
        /// (Partio clamps). Default 0 — requests over the concurrency limit are rejected immediately with 429
        /// rather than queued; a queued request that waits past the endpoint timeout returns 504.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 0;

        /// <summary>
        /// Maximum context window (in tokens) of this completion model. A first-class Partio endpoint field
        /// (<c>ContextSize</c>); reads fall back to the legacy <c>contextSize</c> tag for endpoints created
        /// before the field existed. Drives automatic chat conversation compression once the message history
        /// approaches the window. 0 disables compression.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Maximum upstream request timeout in milliseconds (Partio clamps to >= 1).</summary>
        public int MaximumTimeoutMs { get; set; } = 60000;

        /// <summary>Whether Partio runs background health checks against this endpoint.</summary>
        public bool HealthCheckEnabled { get; set; } = false;

        /// <summary>Health check probe URL. Left blank lets Partio derive it from the endpoint + API format.</summary>
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
    }
}
