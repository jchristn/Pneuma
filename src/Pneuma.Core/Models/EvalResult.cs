namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// The outcome of evaluating one fact in a run: the answer the pipeline produced and the judge's verdict,
    /// score, and reasoning.
    /// </summary>
    public class EvalResult
    {
        #region Public-Members

        /// <summary>Result identifier (prefix "eres_").</summary>
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

        /// <summary>The run this result belongs to.</summary>
        public string RunId
        {
            get { return _RunId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(RunId)); _RunId = value; }
        }

        /// <summary>The fact that was evaluated.</summary>
        public string FactId { get; set; } = String.Empty;

        /// <summary>The question that was asked.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>The expected answer.</summary>
        public string ExpectedAnswer { get; set; } = String.Empty;

        /// <summary>The answer the pipeline produced.</summary>
        public string ProducedAnswer { get; set; } = String.Empty;

        /// <summary>The judge's verdict.</summary>
        public EvalVerdictEnum Verdict { get; set; } = EvalVerdictEnum.Unknown;

        /// <summary>A 0–10 score from the judge.</summary>
        public double Score { get; set; } = 0;

        /// <summary>The judge's short reasoning.</summary>
        public string? Reason { get; set; } = null;

        /// <summary>An optional failure-mode label (for example "missing_evidence", "hallucination").</summary>
        public string? FailureMode { get; set; } = null;

        /// <summary>Category carried from the fact, for result filtering.</summary>
        public string? Category { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateEvalResultId();
        private string _TenantId = String.Empty;
        private string _RunId = String.Empty;

        #endregion
    }
}
