namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Metadata accompanying the server settings object: the list of logical sections, the names of the
    /// fields treated as secrets, and the mask used to redact those secret values.
    /// </summary>
    public class SettingsMeta
    {
        /// <summary>The logical sections that make up the settings object.</summary>
        public List<SettingsSectionMeta> Sections { get; set; } = new List<SettingsSectionMeta>();

        /// <summary>The names of the fields whose values are masked as secrets.</summary>
        public List<string> SecretFields { get; set; } = new List<string>();

        /// <summary>The mask string substituted for secret values (for example "********").</summary>
        public string SecretMask { get; set; } = string.Empty;
    }
}
