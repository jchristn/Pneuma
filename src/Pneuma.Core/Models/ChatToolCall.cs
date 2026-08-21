namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A persisted record of one tool call the agentic assistant made while answering a turn. Streamed live to
    /// the caller during the answer and stored here so the History detail view can show the tool trace after
    /// the fact.
    /// </summary>
    public class ChatToolCall
    {
        #region Public-Members

        /// <summary>Tool-call identifier (prefix "tcall_").</summary>
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

        /// <summary>The chat turn this tool call belongs to.</summary>
        public string TurnId
        {
            get { return _TurnId; }
            set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(TurnId)); _TurnId = value; }
        }

        /// <summary>Subject the turn was scoped to, or null.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>The tool that was called.</summary>
        public string ToolName { get; set; } = String.Empty;

        /// <summary>The JSON arguments passed to the tool (may be truncated), or null.</summary>
        public string? ArgumentsJson { get; set; } = null;

        /// <summary>The JSON result the tool returned (may be truncated), or null.</summary>
        public string? OutputJson { get; set; } = null;

        /// <summary>Whether the tool call succeeded.</summary>
        public bool Success { get; set; } = true;

        /// <summary>Elapsed wall-clock time for the tool call, in milliseconds.</summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>Zero-based order of the call within its turn.</summary>
        public int Sequence { get; set; } = 0;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateChatToolCallId();
        private string _TenantId = String.Empty;
        private string _TurnId = String.Empty;

        #endregion
    }
}
