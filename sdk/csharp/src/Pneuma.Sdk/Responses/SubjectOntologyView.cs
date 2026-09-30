namespace Pneuma.Sdk.Responses
{
    using System;
    using Pneuma.Sdk.Models;

    /// <summary>How a subject classifies.</summary>
    public class SubjectOntologyView
    {
        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>"Version" or "Prompt".</summary>
        public string Source { get; set; } = "Prompt";

        /// <summary>Pinned ontology.</summary>
        public Ontology? Ontology { get; set; } = null;

        /// <summary>Pinned version (header).</summary>
        public OntologyVersion? Version { get; set; } = null;

        /// <summary>Definition the classifier sees.</summary>
        public string EffectiveDefinition { get; set; } = string.Empty;

        /// <summary>Classification temperature.</summary>
        public double ClassificationTemperature { get; set; } = 0;

        /// <summary>Whether the cache is on.</summary>
        public bool ClassificationCacheEnabled { get; set; } = true;

        /// <summary>Cached entries this subject stored.</summary>
        public int CacheEntries { get; set; } = 0;

        /// <summary>Quarantined elements.</summary>
        public int QuarantinedCount { get; set; } = 0;

        /// <summary>Taxonomy concepts.</summary>
        public int ConceptCount { get; set; } = 0;
    }
}
