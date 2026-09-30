namespace Pneuma.Core.Ontologies
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A background ontology operation on a subject: validate the stored graph against the pinned version's rules,
    /// re-apply the pinned version's taxonomy to the stored cells, or check how often classification changes when
    /// the same cells are classified twice. Queued in the database and claimed by a worker.
    /// </summary>
    public class OntologyOperation
    {
        #region Public-Members

        /// <summary>Operation identifier (prefix "oop_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Subject identifier.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>What the operation does.</summary>
        public OntologyOperationKindEnum Kind { get; set; } = OntologyOperationKindEnum.Validate;

        /// <summary>State.</summary>
        public OntologyOperationStatusEnum Status { get; set; } = OntologyOperationStatusEnum.Queued;

        /// <summary>The ontology version in effect when the operation ran, if any.</summary>
        public string? OntologyVersionId { get; set; } = null;

        /// <summary>User who started it, if known.</summary>
        public string? RequestedByUserId { get; set; } = null;

        /// <summary>Cells to sample (DriftCheck). Clamped to [1, 1000].</summary>
        public int SampleSize
        {
            get { return _SampleSize; }
            set { _SampleSize = Math.Clamp(value, 1, 1000); }
        }

        /// <summary>Items to process (nodes, cells, or samples), once known.</summary>
        public int Total { get; set; } = 0;

        /// <summary>Items processed so far.</summary>
        public int Processed { get; set; } = 0;

        /// <summary>
        /// Items with a finding: violations found (Validate), cells whose taxonomy links changed (Retag), or cells whose
        /// classification changed between the two runs (DriftCheck).
        /// </summary>
        public int Changed { get; set; } = 0;

        /// <summary>Taxonomy links added (Retag).</summary>
        public int Added { get; set; } = 0;

        /// <summary>Taxonomy links removed (Retag).</summary>
        public int Removed { get; set; } = 0;

        /// <summary>Share of compared cells whose classification changed, 0 to 1 (DriftCheck).</summary>
        public double DriftRate { get; set; } = 0;

        /// <summary>Error message, if failed.</summary>
        public string? Error { get; set; } = null;

        /// <summary>Claim token of the worker processing it.</summary>
        public string? ClaimToken { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC start timestamp, once claimed.</summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>UTC finish timestamp, once finished.</summary>
        public DateTime? FinishedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateOntologyOperationId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private int _SampleSize = 10;

        #endregion
    }
}
