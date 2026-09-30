namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>One taxonomy concept label found in a text.</summary>
    public class TaxonomyMatch
    {
        #region Public-Members

        /// <summary>Key of the matched concept.</summary>
        public string ConceptKey { get; set; } = String.Empty;

        /// <summary>The text that matched, as it appears in the source.</summary>
        public string Text { get; set; } = String.Empty;

        /// <summary>Character offset of the match.</summary>
        public int Start { get; set; } = 0;

        /// <summary>Length of the match in characters.</summary>
        public int Length { get; set; } = 0;

        #endregion
    }
}
