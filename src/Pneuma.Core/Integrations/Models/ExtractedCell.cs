namespace Pneuma.Core.Integrations.Models
{
    using System;

    /// <summary>
    /// A semantic cell (atom) extracted by DocumentAtom.
    /// </summary>
    public class ExtractedCell
    {
        /// <summary>Cell type (for example Text, Table, List, Image).</summary>
        public string Type { get; set; } = "Text";

        /// <summary>Text content of the cell.</summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>Optional title.</summary>
        public string? Title { get; set; } = null;
    }
}
