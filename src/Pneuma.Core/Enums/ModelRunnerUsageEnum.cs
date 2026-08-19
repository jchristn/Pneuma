namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a model runner may be used within Pneuma.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelRunnerUsageEnum
    {
        /// <summary>Used for ingestion classification and summarization.</summary>
        Ingestion,
        /// <summary>Used for servicing user prompts and grounded answers.</summary>
        UserPrompt,
        /// <summary>Available for both ingestion and user prompts.</summary>
        Both
    }
}
