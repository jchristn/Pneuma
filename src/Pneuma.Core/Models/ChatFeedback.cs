namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Enums;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A user's feedback on a single chat answer: a thumbs up/down and/or a free-form comment, tied to the
    /// <see cref="ChatTurnRecord"/> it rates. Backs the dashboard Feedback surface.
    /// </summary>
    public class ChatFeedback
    {
        #region Public-Members

        /// <summary>Feedback identifier (prefix "fbk_").</summary>
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

        /// <summary>The chat turn this feedback rates.</summary>
        public string TurnId
        {
            get { return _TurnId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TurnId)); _TurnId = value; }
        }

        /// <summary>Subject the rated turn belonged to (denormalized for filtering), or null.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Identifier of the user who left the feedback, or null.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Thumbs up/down (or none for a comment-only submission).</summary>
        public FeedbackRatingEnum Rating { get; set; } = FeedbackRatingEnum.None;

        /// <summary>Optional free-form comment.</summary>
        public string? Comment { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateChatFeedbackId();
        private string _TenantId = String.Empty;
        private string _TurnId = String.Empty;

        #endregion
    }
}
