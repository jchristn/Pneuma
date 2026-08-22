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

        /// <summary>
        /// Optional subject to scope the assistant's retrieval to. When set, the search and grounded-answer
        /// tools are restricted to documents belonging to this subject. Null answers over the whole tenant.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Optional conversation thread to attach this turn to. When null, a new thread is created and its id is
        /// returned on the <c>complete</c> event so the client can continue the conversation.
        /// </summary>
        public string? ThreadId { get; set; } = null;

        /// <summary>
        /// Optional per-request facet filter scoping the assistant's retrieval for this turn. Merged with the
        /// subject's default filter (union of required and excluded), so a request narrows — never widens —
        /// the subject default. Null applies only the subject default.
        /// </summary>
        public RetrievalFilter? MetadataFilter { get; set; } = null;

        #endregion
    }
}
