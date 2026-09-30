namespace Pneuma.Sdk.Responses
{
    using System;

    /// <summary>A subject that pins an ontology version.</summary>
    public class OntologySubjectReference
    {
        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = string.Empty;

        /// <summary>Subject display name.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>Pinned version.</summary>
        public string OntologyVersionId { get; set; } = string.Empty;
    }
}
