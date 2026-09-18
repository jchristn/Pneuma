namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// LLM provider families addressed through PolyPrompt.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelRunnerProviderEnum
    {
        /// <summary>OpenAI API.</summary>
        OpenAI,
        /// <summary>Google Gemini API.</summary>
        Gemini,
        /// <summary>Ollama or other local runner.</summary>
        Ollama,
        /// <summary>An OpenAI-compatible endpoint (vLLM, LM Studio, etc.).</summary>
        OpenAICompatible,
        /// <summary>Azure OpenAI Service (requires a deployment name and API version).</summary>
        AzureOpenAI,
        /// <summary>Anthropic API. Completions only; Anthropic exposes no embeddings API.</summary>
        Anthropic,
        /// <summary>Amazon Bedrock (requires an AWS region and static credentials).</summary>
        Bedrock,
        /// <summary>Voyage AI. Embeddings only.</summary>
        VoyageAI,
        /// <summary>Google Vertex AI (requires a project, region, and credential).</summary>
        VertexAI
    }
}
