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
    }
}
