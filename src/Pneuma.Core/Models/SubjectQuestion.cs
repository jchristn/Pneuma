namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// An example question people are expected to ask about a subject. Starter questions are drafted by the new subject
    /// wizard, edited by the user, shown as suggestions on the ask page, and asked by the wizard's coverage check.
    /// </summary>
    public class SubjectQuestion
    {
        #region Public-Members

        /// <summary>Question identifier (prefix "sq_").</summary>
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

        /// <summary>The subject the question is about.</summary>
        public string SubjectId
        {
            get { return _SubjectId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(SubjectId)); _SubjectId = value; }
        }

        /// <summary>The question text.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>The kind of question.</summary>
        public SubjectQuestionKindEnum Kind { get; set; } = SubjectQuestionKindEnum.Fact;

        /// <summary>Display order, starting at 0.</summary>
        public int Position
        {
            get { return _Position; }
            set { _Position = Math.Max(0, value); }
        }

        /// <summary>Whether a model or a person wrote the question.</summary>
        public SubjectQuestionOriginEnum Origin { get; set; } = SubjectQuestionOriginEnum.User;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateSubjectQuestionId();
        private string _TenantId = String.Empty;
        private string _SubjectId = String.Empty;
        private int _Position = 0;

        #endregion
    }
}
