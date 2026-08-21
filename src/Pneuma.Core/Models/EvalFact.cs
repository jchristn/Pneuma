namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A ground-truth fact for RAG evaluation: a question about a subject and the answer a correct system
    /// should produce. Runs answer the question through the real pipeline and judge the result against this.
    /// </summary>
    public class EvalFact
    {
        #region Public-Members

        /// <summary>Fact identifier (prefix "efact_").</summary>
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

        /// <summary>The subject the fact is about.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>The question to ask.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>The expected (correct) answer.</summary>
        public string ExpectedAnswer { get; set; } = String.Empty;

        /// <summary>Optional category/tag for filtering results (for example "biography", "dates").</summary>
        public string? Category { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateEvalFactId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;

        #endregion
    }
}
