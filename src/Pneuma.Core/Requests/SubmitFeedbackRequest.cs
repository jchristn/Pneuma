namespace Pneuma.Core.Requests
{
    using Pneuma.Core.Enums;

    /// <summary>
    /// Request to record a user's feedback on a single chat answer.
    /// </summary>
    public class SubmitFeedbackRequest
    {
        #region Public-Members

        /// <summary>Identifier of the chat turn being rated.</summary>
        public string? TurnId { get; set; } = null;

        /// <summary>Thumbs up/down (or none for a comment-only submission).</summary>
        public FeedbackRatingEnum Rating { get; set; } = FeedbackRatingEnum.None;

        /// <summary>Optional free-form comment.</summary>
        public string? Comment { get; set; } = null;

        #endregion
    }
}
