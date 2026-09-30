namespace Pneuma.Core.Ontologies
{
    using System;

    /// <summary>A label in the taxonomy matcher's trie: the concept it names and, for case-sensitive concepts, its exact words.</summary>
    internal class TaxonomyLabelEntry
    {
        #region Public-Members

        /// <summary>Key of the concept the label belongs to.</summary>
        public string ConceptKey { get; set; } = String.Empty;

        /// <summary>Whether the label must match with its exact case.</summary>
        public bool CaseSensitive { get; set; } = false;

        /// <summary>The label's words joined by single spaces, in their original case.</summary>
        public string ExactWords { get; set; } = String.Empty;

        #endregion
    }
}
