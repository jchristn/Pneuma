namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Persisted chat-turn (history) data access methods.
    /// </summary>
    public interface IChatTurnMethods
    {
        /// <summary>Create a chat-turn record.</summary>
        /// <param name="turn">Turn to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created turn.</returns>
        Task<ChatTurnRecord> CreateAsync(ChatTurnRecord turn, CancellationToken token = default);

        /// <summary>Read a chat turn by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Turn identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Turn, or null if not found.</returns>
        Task<ChatTurnRecord?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate chat turns within a tenant, newest first, optionally scoped to a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Turns, newest first.</returns>
        Task<List<ChatTurnRecord>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default);

        /// <summary>Enumerate a thread's chat turns, oldest first.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The thread's turns, oldest first.</returns>
        Task<List<ChatTurnRecord>> EnumerateByThreadAsync(string tenantId, string threadId, CancellationToken token = default);

        /// <summary>Delete all chat turns for a subject within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Delete all chat turns in a thread within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="threadId">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByThreadAsync(string tenantId, string threadId, CancellationToken token = default);

        /// <summary>Delete chat turns for a subject older than a cutoff (retention pruning). Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="cutoffUtc">Turns created strictly before this instant are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default);
    }
}
