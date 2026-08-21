namespace Pneuma.Core.Requests
{
    /// <summary>
    /// Create or rename a conversation thread.
    /// </summary>
    public class ThreadRequest
    {
        /// <summary>Subject the thread is scoped to, or null for a whole-tenant conversation (create only).</summary>
        public string? SubjectId { get; set; } = null;

        /// <summary>The thread title.</summary>
        public string? Title { get; set; } = null;
    }
}
