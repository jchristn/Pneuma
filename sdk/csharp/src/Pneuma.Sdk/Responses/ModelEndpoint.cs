namespace Pneuma.Sdk.Responses
{
    using Pneuma.Sdk.Enums;

    /// <summary>
    /// An embedding or completion model endpoint available for ingestion. Secret material
    /// (API keys, secret access keys, session tokens) is write-only and is never returned.
    /// </summary>
    public class ModelEndpoint
    {
        /// <summary>Endpoint identifier (prefix "eep_" for embedding, "cep_" for completion).</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Endpoint type: embedding or completion.</summary>
        public ModelCapabilityEnum Type { get; set; } = ModelCapabilityEnum.Completion;

        /// <summary>Operator-facing name of the endpoint.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Model identifier the endpoint targets.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>Base URL / endpoint the model is reached at.</summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>API format the endpoint speaks (for example "OpenAI", "Ollama").</summary>
        public string ApiFormat { get; set; } = string.Empty;

        /// <summary>Provider family that hosts the model.</summary>
        public ModelRunnerProviderEnum Provider { get; set; } = ModelRunnerProviderEnum.OpenAI;

        /// <summary>Azure OpenAI deployment name (Azure OpenAI only).</summary>
        public string? Deployment { get; set; } = null;

        /// <summary>API version (for example the Azure OpenAI api-version).</summary>
        public string? ApiVersion { get; set; } = null;

        /// <summary>Region (for example Bedrock or Vertex AI).</summary>
        public string? Region { get; set; } = null;

        /// <summary>Project identifier (Vertex AI).</summary>
        public string? Project { get; set; } = null;

        /// <summary>Access key identifier (Bedrock). The matching secret access key is write-only.</summary>
        public string? AccessKeyId { get; set; } = null;

        /// <summary>Whether the endpoint is active and available for selection.</summary>
        public bool Active { get; set; } = true;
    }
}
