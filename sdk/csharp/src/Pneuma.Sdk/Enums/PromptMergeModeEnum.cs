namespace Pneuma.Sdk.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a subject's prompt override combines with the corresponding global prompt.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PromptMergeModeEnum
    {
        /// <summary>The override text is appended to the global prompt.</summary>
        Append,
        /// <summary>The override text replaces the global prompt.</summary>
        Replace
    }
}
