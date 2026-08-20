namespace Pneuma.Core.Responses
{
    using Pneuma.Core.Models;

    /// <summary>
    /// A feedback record enriched with the chat turn it rates, so the Feedback surface can show the full
    /// prompt and response alongside the rating and comment without a second request.
    /// </summary>
    public class ChatFeedbackDetail
    {
        #region Public-Members

        /// <summary>The feedback record.</summary>
        public ChatFeedback Feedback { get; set; } = new ChatFeedback();

        /// <summary>The rated chat turn, or null if it has since been pruned.</summary>
        public ChatTurnRecord? Turn { get; set; } = null;

        #endregion
    }
}
