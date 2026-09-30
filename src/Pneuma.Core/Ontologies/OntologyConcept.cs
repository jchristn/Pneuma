namespace Pneuma.Core.Ontologies
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A taxonomy concept in an ontology version (SKOS-shaped): a preferred label, alternative labels, an optional
    /// broader concept, and the node type its graph node gets. Concepts are matched in cell text deterministically
    /// before classification.
    /// </summary>
    public class OntologyConcept
    {
        #region Public-Members

        /// <summary>
        /// Stable key, unique within the version and kept across versions (for example "kubernetes", or a SKOS IRI when
        /// imported). At most 512 characters. Defaults to the preferred label when empty.
        /// </summary>
        public string Key
        {
            get { return String.IsNullOrWhiteSpace(_Key) ? _PrefLabel : _Key; }
            set { _Key = value?.Trim() ?? String.Empty; }
        }

        /// <summary>Preferred label; the name of the concept's graph node. At most 256 characters.</summary>
        public string PrefLabel
        {
            get { return _PrefLabel; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(PrefLabel)); _PrefLabel = value.Trim(); }
        }

        /// <summary>Alternative labels (synonyms, abbreviations) that also match. Never null.</summary>
        public List<string> AltLabels
        {
            get { return _AltLabels; }
            set { _AltLabels = value ?? new List<string>(); }
        }

        /// <summary>Key of the broader (parent) concept, or null for a top concept.</summary>
        public string? BroaderKey { get; set; } = null;

        /// <summary>Optional definition.</summary>
        public string? Definition { get; set; } = null;

        /// <summary>Node type of the concept's graph node. Default "Topic".</summary>
        public string NodeType
        {
            get { return _NodeType; }
            set { _NodeType = String.IsNullOrWhiteSpace(value) ? "Topic" : value.Trim(); }
        }

        /// <summary>Match labels case-sensitively (for acronyms such as "IT"). Default false.</summary>
        public bool CaseSensitive { get; set; } = false;

        #endregion

        #region Private-Members

        private string _Key = String.Empty;
        private string _PrefLabel = String.Empty;
        private string _NodeType = "Topic";
        private List<string> _AltLabels = new List<string>();

        #endregion
    }
}
