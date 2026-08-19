namespace Pneuma.Sdk.Responses
{
    /// <summary>
    /// Metadata describing a single logical section of the server settings object.
    /// </summary>
    public class SettingsSectionMeta
    {
        /// <summary>Machine-readable key identifying the section.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Human-readable label for the section.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Indicates whether changing this section requires a server restart to take effect.</summary>
        public bool RequiresRestart { get; set; } = false;
    }
}
