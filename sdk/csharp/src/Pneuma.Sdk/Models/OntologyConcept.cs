namespace Pneuma.Sdk.Models
{
    using System;
    using System.Collections.Generic;

    /// <summary>A taxonomy concept (SKOS-shaped).</summary>
    public class OntologyConcept
    {
        /// <summary>Stable key.</summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>Preferred label.</summary>
        public string PrefLabel { get; set; } = string.Empty;

        /// <summary>Alternative labels.</summary>
        public List<string> AltLabels { get; set; } = new List<string>();

        /// <summary>Key of the broader concept.</summary>
        public string? BroaderKey { get; set; } = null;

        /// <summary>Definition.</summary>
        public string? Definition { get; set; } = null;

        /// <summary>Node type of the concept's graph node.</summary>
        public string NodeType { get; set; } = "Topic";

        /// <summary>Match labels case-sensitively.</summary>
        public bool CaseSensitive { get; set; } = false;
    }
}
