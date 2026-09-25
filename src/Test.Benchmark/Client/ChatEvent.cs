namespace Test.Benchmark.Client
{
    using System.Collections.Generic;

    /// <summary>
    /// A server-sent event from <c>POST /v1.0/chat/stream</c>. Only the fields the harness reads are declared.
    /// </summary>
    public class ChatEvent
    {
        #region Public-Members

        /// <summary>
        /// delta, tool_call, tool_result, compacting, complete, or error.
        /// </summary>
        public string? Type { get; set; } = null;

        /// <summary>
        /// Final answer (complete).
        /// </summary>
        public string? Answer { get; set; } = null;

        /// <summary>
        /// Error message (error).
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// Answering model (complete).
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Time to first token (complete).
        /// </summary>
        public long TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>
        /// Tool trace (complete).
        /// </summary>
        public List<ChatToolCall>? ToolCalls { get; set; } = null;

        /// <summary>
        /// Link citations (complete).
        /// </summary>
        public List<ChatCitation>? Citations { get; set; } = null;

        #endregion
    }
}
