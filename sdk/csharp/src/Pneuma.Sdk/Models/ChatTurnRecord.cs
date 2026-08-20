namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A persisted record of a single completed chat turn with its model, token, and timing telemetry.
    /// </summary>
    public class ChatTurnRecord
    {
        /// <summary>Turn identifier (prefix "trn_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Subject the chat was scoped to, or null.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Identifier of the user who asked, or null.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>The user's question.</summary>
        public string Question { get; set; } = string.Empty;

        /// <summary>The assistant's answer.</summary>
        public string Answer { get; set; } = string.Empty;

        /// <summary>Captured model reasoning, if any.</summary>
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

        /// <summary>Citations drawn on for the answer, serialized as a JSON array.</summary>
        public string? CitationsJson { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
