namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>
    /// A Partio model endpoint surfaced through the Pneuma model-runner proxy. Pneuma stores no local
    /// model state; this is a pass-through view of a Partio embedding or completion endpoint.
    /// </summary>
    public class ModelEndpointDto
    {
        #region Public-Members

        /// <summary>Endpoint identifier (Partio endpoint id).</summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>Endpoint type: "Embedding" or "Completion".</summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>Human-readable name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Upstream provider URL.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>API format (e.g. "Ollama").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>Whether the endpoint is active.</summary>
        public bool Active { get; set; } = false;

        /// <summary>Maximum number of concurrent requests Partio will send to this endpoint. Minimum 1. Default 2.</summary>
        public int MaxConcurrentRequests { get; set; } = 2;

        /// <summary>UTC creation timestamp (synthetic; endpoints have no Pneuma-side creation time).</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
