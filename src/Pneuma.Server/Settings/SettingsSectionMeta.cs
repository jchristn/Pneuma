namespace Pneuma.Server.Settings
{
    /// <summary>
    /// Restart annotation for a single top-level settings section.
    /// </summary>
    public class SettingsSectionMeta
    {
        #region Public-Members

        /// <summary>The camelCase section key as it appears in the settings payload (e.g. "database").</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Human-readable section label for the form.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Whether editing this section requires a server restart to take effect.</summary>
        public bool RequiresRestart { get; set; } = false;

        #endregion
    }
}
