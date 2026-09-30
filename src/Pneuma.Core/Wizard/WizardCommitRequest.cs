namespace Pneuma.Core.Wizard
{
    using System;

    /// <summary>A request to create a subject from a finished wizard draft.</summary>
    public class WizardCommitRequest
    {
        #region Public-Members

        /// <summary>The finished draft.</summary>
        public SubjectWizardDraft Draft
        {
            get { return _Draft; }
            set { _Draft = value ?? new SubjectWizardDraft(); }
        }

        /// <summary>Completion model endpoint for answering and ingestion; null uses the tenant's first active completion endpoint.</summary>
        public string? InferenceModel { get; set; } = null;

        /// <summary>Embedding model endpoint; null uses the tenant's first active embedding endpoint.</summary>
        public string? EmbeddingModel { get; set; } = null;

        /// <summary>RecallDB collection; null uses the default collection.</summary>
        public string? Collection { get; set; } = null;

        /// <summary>How to save the ontology. Default Approve; lowered to what the caller may do, with a warning.</summary>
        public WizardOntologyModeEnum OntologyMode { get; set; } = WizardOntologyModeEnum.Approve;

        /// <summary>Show the subject in the consumer chat. Default true.</summary>
        public bool PublishedForChat { get; set; } = true;

        #endregion

        #region Private-Members

        private SubjectWizardDraft _Draft = new SubjectWizardDraft();

        #endregion
    }
}
