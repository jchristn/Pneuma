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
        /// Default 2.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>
        /// Maximum context window (in tokens) of a completion model. Drives automatic chat conversation
        /// compression once the message history approaches the window. 0 disables compression.
        /// </summary>
        public int ContextSize { get; set; } = 0;

        #endregion
    }
}
