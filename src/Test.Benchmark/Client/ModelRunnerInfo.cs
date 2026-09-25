namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// A model endpoint as listed by <c>GET /v1.0/model-runners</c>. Both the stored-runner and the endpoint-DTO
    /// field names are accepted, since only the ones present are populated.
    /// </summary>
    public class ModelRunnerInfo
    {
        #region Public-Members

        /// <summary>
        /// Endpoint id.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Embedding or Completion (endpoint DTO).
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Provider name.
        /// </summary>
        public string? Provider { get; set; } = null;

        /// <summary>
        /// Display name.
        /// </summary>
        public string? Name { get; set; } = null;

        /// <summary>
        /// Model name (endpoint DTO).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Base URL (endpoint DTO).
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Base URL (stored runner).
        /// </summary>
        public string? BaseUrl { get; set; } = null;

        /// <summary>
        /// Completion model (stored runner).
        /// </summary>
        public string? DefaultModel { get; set; } = null;

        /// <summary>
        /// Embedding model (stored runner).
        /// </summary>
        public string? DefaultEmbeddingModel { get; set; } = null;

        /// <summary>
        /// Capabilities (stored runner).
        /// </summary>
        public List<string>? Capabilities { get; set; } = null;

        /// <summary>
        /// Whether the endpoint is active.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Public-Methods

        /// <summary>
        /// The effective model name for a capability.
        /// </summary>
        /// <param name="embedding">True for the embedding model.</param>
        /// <returns>The model name, or null.</returns>
        public string? ModelFor(bool embedding)
        {
            if (!string.IsNullOrEmpty(Model)) return Model;
            return embedding ? DefaultEmbeddingModel : DefaultModel;
        }

        /// <summary>
        /// True when this endpoint serves the capability.
        /// </summary>
        /// <param name="embedding">True for embeddings, false for completions.</param>
        /// <returns>True when it does.</returns>
        public bool Serves(bool embedding)
        {
            string wanted = embedding ? "Embedding" : "Completion";
            if (!string.IsNullOrEmpty(Type)) return string.Equals(Type, wanted, System.StringComparison.OrdinalIgnoreCase);
            if (Capabilities != null) return Capabilities.Exists(c => string.Equals(c, wanted, System.StringComparison.OrdinalIgnoreCase));
            return false;
        }

        /// <summary>
        /// The endpoint base URL.
        /// </summary>
        /// <returns>The URL, or an empty string.</returns>
        public string Url()
        {
            return Endpoint ?? BaseUrl ?? string.Empty;
        }

        #endregion
    }
}
