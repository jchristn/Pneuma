namespace Pneuma.Sdk.Models
{
    /// <summary>A feedback record enriched with the chat turn it rates.</summary>
    public class ChatFeedbackDetail
    {
        /// <summary>The feedback record.</summary>
        public ChatFeedback? Feedback { get; set; } = null;

        /// <summary>The rated chat turn, or null if it has since been pruned.</summary>
        public ChatTurnRecord? Turn { get; set; } = null;
    }
}
