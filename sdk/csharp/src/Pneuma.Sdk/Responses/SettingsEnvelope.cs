namespace Pneuma.Sdk.Responses
{
    using System.Text.Json;

    /// <summary>
    /// Envelope returned by the settings endpoints. On a read, <see cref="Settings"/> carries the full
    /// settings object with secrets masked. On an update, <see cref="RestartRequired"/> and
    /// <see cref="Message"/> describe the outcome.
    /// </summary>
    public class SettingsEnvelope
    {
        /// <summary>Indicates whether the operation succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Indicates whether a server restart is required for the change to take full effect.</summary>
        public bool RestartRequired { get; set; } = false;

        /// <summary>Optional human-readable message describing the outcome.</summary>
        public string? Message { get; set; } = null;

        /// <summary>The full settings object, with secret fields masked. Null when not returned.</summary>
        public JsonElement? Settings { get; set; } = null;

        /// <summary>Metadata describing the settings object. Null when not returned.</summary>
        public SettingsMeta? Meta { get; set; } = null;
    }
}
