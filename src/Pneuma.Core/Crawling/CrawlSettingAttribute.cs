namespace Pneuma.Core.Crawling
{
    using System;

    /// <summary>
    /// Describes one crawler setting: its label and help for the dashboard form, whether it is required, its range, its
    /// allowed values, and whether it is a secret (stored encrypted, never returned). The settings schema, validation,
    /// and persistence are all derived from these attributes, so a crawler's settings class is the single definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public sealed class CrawlSettingAttribute : Attribute
    {
        #region Public-Members

        /// <summary>Human-readable label.</summary>
        public string Label { get; }

        /// <summary>Help text explaining what the setting does and its default.</summary>
        public string Help { get; }

        /// <summary>True when the setting must have a value.</summary>
        public bool Required { get; set; } = false;

        /// <summary>Minimum for a numeric setting; ignored when <see cref="Max"/> is not above it.</summary>
        public long Min { get; set; } = 0;

        /// <summary>Maximum for a numeric setting; 0 means no range is enforced.</summary>
        public long Max { get; set; } = 0;

        /// <summary>Allowed values for a choice setting, or null for free text.</summary>
        public string[]? Options { get; set; } = null;

        /// <summary>True for a secret (password, token, key): stored encrypted, write-only, redacted in logs.</summary>
        public bool Secret { get; set; } = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>Instantiate the attribute.</summary>
        /// <param name="label">Human-readable label.</param>
        /// <param name="help">Help text.</param>
        public CrawlSettingAttribute(string label, string help)
        {
            Label = label ?? String.Empty;
            Help = help ?? String.Empty;
        }

        #endregion
    }
}
