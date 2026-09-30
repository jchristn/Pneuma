namespace Pneuma.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Pneuma.Core.Ontologies;

    /// <summary>A tenant ontology with its versions (newest first) and the subjects that pin them.</summary>
    public class OntologyDetail
    {
        #region Public-Members

        /// <summary>The ontology.</summary>
        public TenantOntology Ontology { get; set; } = null!;

        /// <summary>Its versions (headers with counts), newest first.</summary>
        public List<OntologyVersion> Versions { get; set; } = new List<OntologyVersion>();

        /// <summary>Subjects pinned to one of its versions.</summary>
        public List<OntologySubjectReference> PinnedSubjects { get; set; } = new List<OntologySubjectReference>();

        #endregion
    }
}
