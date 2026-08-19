namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Wraps a settings read or write response with the settings payload and descriptive metadata.
    /// </summary>
    public class SettingsEnvelope
    {
        #region Public-Members

        /// <summary>Whether a write succeeded. Always true for reads.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Whether the server must be restarted for the saved changes to take effect.</summary>
        public bool RestartRequired { get; set; } = false;

        /// <summary>Optional human-readable message (e.g. shown after a write).</summary>
        public string? Message { get; set; } = null;

        /// <summary>The settings payload, with secrets masked. Present on reads; null on writes.</summary>
        public AppSettings? Settings { get; set; } = null;

        /// <summary>Descriptive metadata about the settings sections and secret fields.</summary>
        public SettingsMeta? Meta { get; set; } = null;

        #endregion
    }
}
