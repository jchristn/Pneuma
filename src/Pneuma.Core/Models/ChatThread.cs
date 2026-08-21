namespace Pneuma.Core.Models
{
    using System;
    using Pneuma.Core.Helpers;

    /// <summary>
    /// A conversation thread: an ordered group of chat turns the user carried on together. Threads let the
    /// dashboards present chat history as named conversations rather than isolated turns.
    /// </summary>
    public class ChatThread
    {
        #region Public-Members

        /// <summary>Thread identifier (prefix "thr_").</summary>
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

        /// <summary>Subject the conversation is scoped to, or null for a whole-tenant chat.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Identifier of the user who started the thread, or null when unknown.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Human-readable title (auto-generated from the first turn, or renamed by the user).</summary>
        public string Title { get; set; } = "New conversation";

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>UTC timestamp of the most recent turn in the thread.</summary>
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateChatThreadId();
        private string _TenantId = String.Empty;

        #endregion
    }
}
