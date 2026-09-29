namespace Pneuma.Core.Crawling
{
    using System;
    using System.Collections.Generic;

    /// <summary>One field of a crawler's settings schema, as rendered by the dashboard's crawl plan form.</summary>
    public class CrawlSettingField
    {
        #region Public-Members

        /// <summary>Property name in camelCase (the JSON name).</summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>Human-readable label.</summary>
        public string Label { get; set; } = String.Empty;

        /// <summary>Help text.</summary>
        public string Help { get; set; } = String.Empty;

        /// <summary>Field kind: string, integer, boolean, list (of strings), choice, or secret.</summary>
        public string Kind { get; set; } = "string";

        /// <summary>True when the field must have a value.</summary>
        public bool Required { get; set; } = false;

        /// <summary>Minimum for an integer field, or null.</summary>
        public long? Min { get; set; } = null;

        /// <summary>Maximum for an integer field, or null.</summary>
        public long? Max { get; set; } = null;

        /// <summary>Allowed values for a choice field; empty otherwise.</summary>
        public List<string> Options { get; set; } = new List<string>();

        /// <summary>The default value as text (lists joined with newlines); null for secrets.</summary>
        public string? Default { get; set; } = null;

        #endregion
    }
}
