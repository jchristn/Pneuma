namespace Test.Benchmark.Client
{
    /// <summary>
    /// Body for <c>POST /v1.0/model-runners</c>.
    /// </summary>
    public class CreateModelRunnerBody
    {
        #region Public-Members

        /// <summary>
        /// Embedding or Completion.
        /// </summary>
        public string Type { get; set; } = "Embedding";

        /// <summary>
        /// Provider (Ollama, OpenAICompatible, ...).
        /// </summary>
        public string Provider { get; set; } = "Ollama";

        /// <summary>
        /// Display name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Model name.
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Base URL.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Active flag.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Concurrent connections to the endpoint.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 4;

        /// <summary>
        /// Requests allowed to wait for a slot.
        /// </summary>
        public int MaxQueueDepth { get; set; } = 256;

        /// <summary>
        /// Per-request timeout.
        /// </summary>
        public int MaximumTimeoutMs { get; set; } = 600000;

        /// <summary>
        /// Health checks are off for bench endpoints (the stub has no health route semantics).
        /// </summary>
        public bool HealthCheckEnabled { get; set; } = false;

        #endregion
    }
}
