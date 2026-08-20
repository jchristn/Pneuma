namespace Pneuma.Sdk.Models
{
    using System;

    /// <summary>
    /// A user's feedback on a single chat answer: a thumbs up/down and/or a comment tied to a chat turn.
    /// </summary>
    public class ChatFeedback
    {
        /// <summary>Feedback identifier (prefix "fbk_").</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Owning tenant identifier.</summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>The chat turn this feedback rates.</summary>
        public string TurnId { get; set; } = string.Empty;

        /// <summary>Subject the rated turn belonged to, or null.</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>Identifier of the user who left the feedback, or null.</summary>
        public string? UserId { get; set; } = null;

        /// <summary>Thumbs up/down (or "None" for a comment-only submission): "Up", "Down", or "None".</summary>
        public string Rating { get; set; } = "None";

        /// <summary>Optional free-form comment.</summary>
        public string? Comment { get; set; } = null;

        /// <summary>UTC creation timestamp.</summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    }
}
