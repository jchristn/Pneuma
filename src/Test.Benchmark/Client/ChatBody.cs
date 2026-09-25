namespace Test.Benchmark.Client
{
    using System.Collections.Generic;
    using Test.Benchmark.Datasets;

    /// <summary>
    /// Body for <c>POST /v1.0/chat/stream</c>.
    /// </summary>
    public class ChatBody
    {
        #region Public-Members

        /// <summary>
        /// Conversation so far (the harness sends one user message).
        /// </summary>
        public List<ChatMessageBody> Messages { get; set; } = new List<ChatMessageBody>();

        /// <summary>
        /// Maximum results per tool search (null = server default).
        /// </summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>
        /// Subject scope.
        /// </summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>
        /// Optional metadata filter.
        /// </summary>
        public QueryFilter? MetadataFilter { get; set; } = null;

        #endregion
    }
}
