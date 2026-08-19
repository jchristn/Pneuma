namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// A capability a model runner exposes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ModelCapabilityEnum
    {
        /// <summary>Text completion / chat.</summary>
        Completion,
        /// <summary>Text embeddings.</summary>
        Embedding
    }
}
