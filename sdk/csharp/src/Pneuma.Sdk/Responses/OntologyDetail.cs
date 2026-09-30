namespace Pneuma.Sdk.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Sdk.Models;

    /// <summary>An ontology with its versions and pinning subjects.</summary>
    public class OntologyDetail
    {
        /// <summary>The ontology.</summary>
        public Ontology Ontology { get; set; } = new Ontology();

        /// <summary>Versions, newest first.</summary>
        public List<OntologyVersion> Versions { get; set; } = new List<OntologyVersion>();

        /// <summary>Pinning subjects.</summary>
        public List<OntologySubjectReference> PinnedSubjects { get; set; } = new List<OntologySubjectReference>();
    }
}
