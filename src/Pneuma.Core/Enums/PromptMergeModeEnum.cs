namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// How a per-subject prompt override combines with the global default prompt.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PromptMergeModeEnum
    {
        /// <summary>Append the override after the global base ("global base" + blank line + override).</summary>
        Append,
        /// <summary>Replace the global base entirely with the override.</summary>
        Replace
    }
}
