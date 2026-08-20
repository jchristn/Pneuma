namespace Pneuma.Core.Database.Interfaces
{
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Chat feedback (thumbs up/down + comments) data access methods.
    /// </summary>
    public interface IChatFeedbackMethods
    {
        /// <summary>Create a feedback record.</summary>
        /// <param name="feedback">Feedback to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created feedback.</returns>
        Task<ChatFeedback> CreateAsync(ChatFeedback feedback, CancellationToken token = default);

        /// <summary>Read a feedback record by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Feedback identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feedback, or null if not found.</returns>
        Task<ChatFeedback?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate feedback within a tenant, newest first, optionally scoped to a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Feedback, newest first.</returns>
        Task<List<ChatFeedback>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default);

        /// <summary>Delete all feedback for a subject within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
