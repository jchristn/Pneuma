namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A persisted record of a single completed chat turn (one question and its answer) with the model,
    /// token, and timing telemetry captured at the time. Backs the dashboard History surface and is the
    /// anchor a piece of <see cref="ChatFeedback"/> refers to.
    /// </summary>
    public class ChatTurnRecord
    {
        #region Public-Members

        /// <summary>Turn identifier (prefix "trn_").</summary>
        public string Id
        {
            get { return _Id; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id)); _Id = value; }
        }

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId
        {
            get { return _TenantId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TenantId)); _TenantId = value; }
        }

        /// <summary>Subject the chat was scoped to, or null for a whole-tenant chat.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Identifier of the user who asked, or null when unauthenticated/unknown.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>The user's question.</summary>
        public string Question { get; set; } = String.Empty;

        /// <summary>The assistant's answer.</summary>
        public string Answer { get; set; } = String.Empty;

        /// <summary>Captured model reasoning ("thinking"), if any. Always stored; display is gated per subject.</summary>
        public string? Thinking { get; set; } = null;

        /// <summary>Answering model identifier.</summary>
        public string? Model { get; set; } = null;

        /// <summary>Prompt tokens consumed.</summary>
        public int PromptTokens { get; set; } = 0;

        /// <summary>Completion tokens produced.</summary>
        public int CompletionTokens { get; set; } = 0;

        /// <summary>Total tokens.</summary>
        public int TotalTokens { get; set; } = 0;

        /// <summary>Milliseconds until the first token streamed.</summary>
        public double TimeToFirstTokenMs { get; set; } = 0;

        /// <summary>Total generation time in milliseconds.</summary>
        public double GenerationMs { get; set; } = 0;

        /// <summary>Milliseconds spent inside model thinking.</summary>
        public double ThinkingMs { get; set; } = 0;

        /// <summary>The answering model's context window in tokens (0 when unknown).</summary>
        public int ContextSize { get; set; } = 0;

        /// <summary>Citations drawn on for the answer, serialized as a JSON array (schemaless shape).</summary>
        public string? CitationsJson { get; set; } = null;

        /// <summary>
        /// Structured per-stage performance telemetry (a serialized <see cref="TurnPerformance"/>), or null for
        /// turns recorded before telemetry capture. Schemaless payload column.
        /// </summary>
        public string? PerformanceJson { get; set; } = null;

        /// <summary>Schema version of <see cref="PerformanceJson"/> (0 when absent).</summary>
        public int PerformanceSchemaVersion { get; set; } = 0;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateChatTurnId();
        private string _TenantId = String.Empty;

        #endregion
    }
}
