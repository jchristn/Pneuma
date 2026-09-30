namespace Pneuma.Core.Responses
{
    using System;
    using Pneuma.Core.Ontologies;

    /// <summary>How a subject classifies: its pinned ontology version (if any), the definition the classifier sees, and its classification settings and findings.</summary>
    public class SubjectOntologyView
    {
        #region Public-Members

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>"Version" when an approved ontology version is pinned; "Prompt" when the ontology.definition prompt chain is used.</summary>
        public string Source { get; set; } = "Prompt";

        /// <summary>The pinned ontology, or null.</summary>
        public TenantOntology? Ontology { get; set; } = null;

        /// <summary>The pinned version (header with counts), or null.</summary>
        public OntologyVersion? Version { get; set; } = null;

        /// <summary>The ontology definition the classifier sees for this subject.</summary>
        public string EffectiveDefinition { get; set; } = String.Empty;

        /// <summary>Classification temperature.</summary>
        public double ClassificationTemperature { get; set; } = 0;

        /// <summary>Whether classification results are cached and reused.</summary>
        public bool ClassificationCacheEnabled { get; set; } = true;

        /// <summary>Cached classification results this subject stored.</summary>
        public int CacheEntries { get; set; } = 0;

        /// <summary>Quarantined elements waiting for review.</summary>
        public int QuarantinedCount { get; set; } = 0;

        /// <summary>Taxonomy concepts in the pinned version.</summary>
        public int ConceptCount { get; set; } = 0;

        #endregion
    }
}
