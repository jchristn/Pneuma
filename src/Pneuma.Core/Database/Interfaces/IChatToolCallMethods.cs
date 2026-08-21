namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Persisted chat tool-call (agentic trace) data access methods.
    /// </summary>
    public interface IChatToolCallMethods
    {
        /// <summary>Create many tool-call rows in one call. A null or empty list is a no-op.</summary>
        /// <param name="calls">Tool calls to create.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateManyAsync(List<ChatToolCall> calls, CancellationToken token = default);

        /// <summary>Enumerate a turn's tool calls in call order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="turnId">Turn identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The turn's tool calls, ordered by sequence.</returns>
        Task<List<ChatToolCall>> EnumerateByTurnAsync(string tenantId, string turnId, CancellationToken token = default);

        /// <summary>Delete all tool calls for a subject within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Delete all tool calls for a turn within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="turnId">Turn identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteByTurnAsync(string tenantId, string turnId, CancellationToken token = default);

        /// <summary>Delete a subject's tool calls older than a cutoff (retention pruning). Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="cutoffUtc">Tool calls created strictly before this instant are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default);
    }
}
