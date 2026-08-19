namespace Pneuma.Core.Requests
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A multi-turn request to the agentic chat assistant. The final user turn is the current question;
    /// prior turns provide conversational context. The assistant may call Pneuma's read tools while
    /// answering. <see cref="MaxResults"/> bounds retrieval within tool calls that page or search.
    /// </summary>
    public class ChatRequest
    {
        #region Public-Members

        /// <summary>The conversation so far, oldest first. Must contain at least one user turn.</summary>
        public List<ChatTurn> Messages { get; set; } = new List<ChatTurn>();

        /// <summary>Maximum supporting sources for tool retrieval (clamped 1..20 by the handler).</summary>
        public int MaxResults { get; set; } = 8;

        #endregion
    }
}
