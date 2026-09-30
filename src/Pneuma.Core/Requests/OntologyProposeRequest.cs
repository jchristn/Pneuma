namespace Pneuma.Core.Requests
{
    using System;

    /// <summary>Request body to have the inference model propose a new draft version from sample content.</summary>
    public class OntologyProposeRequest
    {
        #region Public-Members

        /// <summary>Subject to sample cells from (and whose inference model to use), or null.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Sample text to use instead of (or as well as) a subject's cells.</summary>
        public string? SampleText { get; set; } = null;

        /// <summary>Model runner to use, or null for the subject's inference model.</summary>
        public string? ModelRunnerId { get; set; } = null;

        /// <summary>Cells to sample from the subject. Default 20; capped by the server's Ontology.MaxProposalSampleCells.</summary>
        public int SampleCells { get; set; } = 20;

        /// <summary>Extra instructions for the model (for example the domain, or what to focus on).</summary>
        public string? Instructions { get; set; } = null;

        /// <summary>Language for type descriptions, for example "English" or "de". Null uses English.</summary>
        public string? Language { get; set; } = null;

        /// <summary>A version to extend (its types are kept and the proposal adds to them), or null to propose from scratch.</summary>
        public string? BasedOnVersionId { get; set; } = null;

        #endregion
    }
}
