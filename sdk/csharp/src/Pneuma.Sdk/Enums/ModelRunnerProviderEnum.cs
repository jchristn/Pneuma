namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// LLM / embedding provider families.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelRunnerProviderEnum
    {
        /// <summary>OpenAI API.</summary>
        OpenAI,
        /// <summary>An OpenAI-compatible endpoint (vLLM, LM Studio, etc.).</summary>
        OpenAICompatible,
        /// <summary>Google Gemini API.</summary>
        Gemini,
        /// <summary>Ollama or other local runner.</summary>
        Ollama,
        /// <summary>Azure OpenAI Service.</summary>
        AzureOpenAI,
        /// <summary>Anthropic API.</summary>
        Anthropic,
        /// <summary>Amazon Bedrock.</summary>
        Bedrock,
        /// <summary>Voyage AI.</summary>
        VoyageAI,
        /// <summary>Google Vertex AI.</summary>
        VertexAI
    }
}
