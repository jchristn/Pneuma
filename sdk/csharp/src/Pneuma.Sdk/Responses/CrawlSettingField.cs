namespace Pneuma.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>One field of a crawler's settings schema.</summary>
    public class CrawlSettingField
    {
        /// <summary>Property name in camelCase.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Label.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>Help text.</summary>
        public string Help { get; set; } = string.Empty;

        /// <summary>string, integer, boolean, list, choice, or secret.</summary>
        public string Kind { get; set; } = "string";

        /// <summary>True when required.</summary>
        public bool Required { get; set; } = false;

        /// <summary>Minimum for an integer.</summary>
        public long? Min { get; set; } = null;

        /// <summary>Maximum for an integer.</summary>
        public long? Max { get; set; } = null;

        /// <summary>Allowed values for a choice.</summary>
        public List<string> Options { get; set; } = new List<string>();

        /// <summary>Default value as text.</summary>
        public string? Default { get; set; } = null;
    }
}
