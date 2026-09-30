namespace Pneuma.Core.Ontologies
{
    using System;
    using Pneuma.Core.Models;

    /// <summary>The result of pinning a subject to an ontology version.</summary>
    public class SubjectPinResult
    {
        #region Public-Members

        /// <summary>The updated subject.</summary>
        public Subject Subject { get; set; } = null!;

        /// <summary>The version pinned before, or null.</summary>
        public string? PreviousVersionId { get; set; } = null;

        /// <summary>Whether the taxonomy the subject tags with changed, so existing cells should be retagged.</summary>
        public bool TaxonomyChanged { get; set; } = false;

        #endregion
    }
}
