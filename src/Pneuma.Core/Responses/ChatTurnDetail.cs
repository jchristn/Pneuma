namespace Pneuma.Core.Responses
{
    using System.Collections.Generic;
    using Pneuma.Core.Models;

    /// <summary>
    /// A persisted chat turn together with any feedback it has received. Backs the History detail view.
    /// </summary>
    public class ChatTurnDetail
    {
        #region Public-Members

        /// <summary>The chat turn.</summary>
        public ChatTurnRecord? Turn { get; set; } = null;

        /// <summary>Feedback recorded against this turn.</summary>
        public List<ChatFeedback> Feedback { get; set; } = new List<ChatFeedback>();

        /// <summary>The agentic tool calls made while producing this turn, in call order.</summary>
        public List<ChatToolCall> ToolCalls { get; set; } = new List<ChatToolCall>();

        #endregion
    }
}
