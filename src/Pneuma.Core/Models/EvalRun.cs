namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// One RAG evaluation run: a set of ground-truth facts for a subject answered through the real pipeline and
    /// judged, with tallies of the outcome.
    /// </summary>
    public class EvalRun
    {
        #region Public-Members

        /// <summary>Run identifier (prefix "erun_").</summary>
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

        /// <summary>The subject evaluated.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>Run status.</summary>
        public EvalRunStatusEnum Status { get; set; } = EvalRunStatusEnum.Pending;

        /// <summary>Optional category filter applied when selecting facts (null = all facts).</summary>
        public string? Category { get; set; } = null;

        /// <summary>Total facts in the run.</summary>
        public int TotalFacts { get; set; } = 0;

        /// <summary>Facts judged Pass.</summary>
        public int PassCount { get; set; } = 0;

        /// <summary>Facts judged Partial.</summary>
        public int PartialCount { get; set; } = 0;

        /// <summary>Facts judged Fail.</summary>
        public int FailCount { get; set; } = 0;

        /// <summary>The judge model used, when known.</summary>
        public string? JudgeModel { get; set; } = null;

        /// <summary>An error message when the run failed.</summary>
        public string? Error { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC completion timestamp, when finished.</summary>
        public DateTime? FinishedUtc { get; set; } = null;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateEvalRunId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;

        #endregion
    }
}
