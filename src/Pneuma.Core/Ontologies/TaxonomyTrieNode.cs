namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>A node of the taxonomy matcher's token trie (one lower-cased word per edge).</summary>
    internal class TaxonomyTrieNode
    {
        #region Public-Members

        /// <summary>Child nodes by lower-cased token.</summary>
        public Dictionary<string, TaxonomyTrieNode> Children { get; } = new Dictionary<string, TaxonomyTrieNode>(StringComparer.Ordinal);

        /// <summary>Labels that end at this node.</summary>
        public List<TaxonomyLabelEntry> Labels { get; } = new List<TaxonomyLabelEntry>();

        #endregion
    }
}
