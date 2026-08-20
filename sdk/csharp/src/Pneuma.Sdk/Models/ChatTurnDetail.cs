namespace Pneuma.Sdk.Models
{
    using System.Collections.Generic;

    /// <summary>A chat turn together with any feedback it has received.</summary>
    public class ChatTurnDetail
    {
        /// <summary>The chat turn.</summary>
        public ChatTurnRecord? Turn { get; set; } = null;

        /// <summary>Feedback recorded against this turn.</summary>
        public List<ChatFeedback> Feedback { get; set; } = new List<ChatFeedback>();
    }
}
