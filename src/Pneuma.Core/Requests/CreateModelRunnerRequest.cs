namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Enums;

    /// <summary>
    /// Request to create or update a model runner. The API key is provided in plaintext and encrypted
    /// server-side.
    /// </summary>
    public class CreateModelRunnerRequest
    {
        /// <summary>Human-readable name.</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Provider family.</summary>
        public ModelRunnerProviderEnum Provider { get; set; } = ModelRunnerProviderEnum.Ollama;

        /// <summary>Base URL of the endpoint.</summary>
        public string BaseUrl { get; set; } = String.Empty;

        /// <summary>Optional API type hint.</summary>
        public string? ApiType { get; set; } = null;

        /// <summary>Plaintext API key (encrypted server-side; never returned).</summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>Capabilities exposed by the runner.</summary>
        public List<ModelCapabilityEnum> Capabilities { get; set; } = new List<ModelCapabilityEnum>();

        /// <summary>How the runner may be used.</summary>
        public ModelRunnerUsageEnum Usage { get; set; } = ModelRunnerUsageEnum.Both;

        /// <summary>Default completion model.</summary>
        public string? DefaultModel { get; set; } = null;

        /// <summary>Default embedding model.</summary>
        public string? DefaultEmbeddingModel { get; set; } = null;

        /// <summary>Whether the runner is active.</summary>
        public bool Active { get; set; } = true;
    }
}
