namespace Pneuma.Sdk.Requests
{
    /// <summary>Request to record a user's feedback on a chat answer.</summary>
    public class SubmitFeedbackRequest
    {
        /// <summary>Identifier of the chat turn being rated.</summary>
        public string? TurnId { get; set; } = null;

        /// <summary>Thumbs up/down (or "None" for a comment-only submission): "Up", "Down", or "None".</summary>
        public string Rating { get; set; } = "None";

        /// <summary>Optional free-form comment.</summary>
        public string? Comment { get; set; } = null;
    }
}
