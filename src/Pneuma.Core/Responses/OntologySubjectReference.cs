namespace Pneuma.Core.Responses
{
    using System;

    /// <summary>A subject that pins an ontology version.</summary>
    public class OntologySubjectReference
    {
        #region Public-Members

        /// <summary>Subject identifier.</summary>
        public string SubjectId { get; set; } = String.Empty;

        /// <summary>Subject display name.</summary>
        public string DisplayName { get; set; } = String.Empty;

        /// <summary>The pinned version.</summary>
        public string OntologyVersionId { get; set; } = String.Empty;

        #endregion
    }
}
