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

        /// <summary>Heading level when the cell is itself a heading (1 for a top-level heading); 0 otherwise.</summary>
        public int HeaderLevel { get; set; } = 0;

        /// <summary>
        /// The headings above the cell, outermost first, joined with " > " (for example "Install > Linux"). Null when
        /// the document has no headings above the cell.
        /// </summary>
        public string? HeadingPath { get; set; } = null;
    }
}
