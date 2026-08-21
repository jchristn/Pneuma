namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Persisted conversation-thread data access methods.
    /// </summary>
    public interface IChatThreadMethods
    {
        /// <summary>Create a thread.</summary>
        /// <param name="thread">Thread to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created thread.</returns>
        Task<ChatThread> CreateAsync(ChatThread thread, CancellationToken token = default);

        /// <summary>Read a thread by identifier within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Thread, or null if not found.</returns>
        Task<ChatThread?> ReadAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Enumerate a tenant's threads, most-recently-active first, optionally scoped to a subject.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Threads, most recent activity first.</returns>
        Task<List<ChatThread>> EnumerateAsync(string tenantId, string? subjectId, CancellationToken token = default);

        /// <summary>Update a thread's title and last-activity timestamp.</summary>
        /// <param name="thread">Thread to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated thread.</returns>
        Task<ChatThread> UpdateAsync(ChatThread thread, CancellationToken token = default);

        /// <summary>Delete a thread by identifier within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="id">Thread identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteAsync(string tenantId, string id, CancellationToken token = default);

        /// <summary>Delete all threads for a subject within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);
    }
}
