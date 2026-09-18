namespace Pneuma.Core.Enums
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// Where a resolved prompt's effective content came from.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PromptSourceEnum
    {
        /// <summary>The global (or tenant) default prompt was used with no per-subject override.</summary>
        Global,
        /// <summary>A per-subject override was applied on top of (or in place of) the global default.</summary>
        SubjectOverride
    }
}
