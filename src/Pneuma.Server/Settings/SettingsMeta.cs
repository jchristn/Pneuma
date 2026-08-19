namespace Pneuma.Server.Settings
{
    using System.Collections.Generic;

    /// <summary>
    /// Descriptive metadata guiding a settings form: which sections need a restart and which fields are secret.
    /// </summary>
    public class SettingsMeta
    {
        #region Public-Members

        /// <summary>Per-section restart annotations.</summary>
        public List<SettingsSectionMeta> Sections { get; set; } = new List<SettingsSectionMeta>();

        /// <summary>Dot-path field names whose values are secret (masked on read).</summary>
        public List<string> SecretFields { get; set; } = new List<string>();

        /// <summary>The sentinel used to mask secret values. Submitting it unchanged preserves the stored secret.</summary>
        public string SecretMask { get; set; } = "********";

        #endregion
    }
}
