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
        OpenAICompatible
    }
}
