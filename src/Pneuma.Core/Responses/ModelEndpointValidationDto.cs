namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// The result of actively validating a model endpoint by exercising it end to end. Unlike passive health
    /// (a reachability probe), a validation issues real requests: a completion endpoint is checked with a basic
    /// completion and a tool-calling round-trip (the same path agentic chat uses), and an embedding endpoint is
    /// checked with a live embedding request. The endpoint passes only when every <see cref="Checks"/> entry does.
    /// </summary>
    public class ModelEndpointValidationDto
    {
        #region Public-Members

        /// <summary>Partio endpoint identifier that was validated.</summary>
        public string EndpointId { get; set; } = String.Empty;

        /// <summary>Human-readable endpoint name.</summary>
        public string? Name { get; set; } = null;

        /// <summary>Endpoint type ("Embedding" or "Completion").</summary>
        public string Type { get; set; } = String.Empty;

        /// <summary>Underlying model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Upstream provider URL.</summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>API format (e.g. "Ollama", "OpenAI", "Gemini").</summary>
        public string? ApiFormat { get; set; } = null;

        /// <summary>Whether every check passed.</summary>
        public bool Ok { get; set; } = false;

        /// <summary>UTC time the validation ran.</summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>The individual probes performed, in execution order.</summary>
        public List<ModelEndpointValidationCheck> Checks { get; set; } = new List<ModelEndpointValidationCheck>();

        #endregion
    }
}
