namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Persisted chat-turn performance-event data access methods. One row per stage of a chat turn's answer
    /// pipeline, written alongside the turn's serialized telemetry for efficient analytics aggregation.
    /// </summary>
    public interface IChatTurnPerfEventMethods
    {
        /// <summary>Create many performance-event rows in one call. A null or empty list is a no-op.</summary>
        /// <param name="events">Events to create.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateManyAsync(List<ChatTurnPerfEvent> events, CancellationToken token = default);

        /// <summary>Enumerate a turn's performance events in stored order.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="turnId">Turn identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The turn's performance events.</returns>
        Task<List<ChatTurnPerfEvent>> EnumerateByTurnAsync(string tenantId, string turnId, CancellationToken token = default);

        /// <summary>Enumerate a subject's performance events created on or after a cutoff (for analytics).</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject to scope to, or null for all subjects in the tenant.</param>
        /// <param name="sinceUtc">Only events created on or after this instant are returned.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Matching performance events, newest first.</returns>
        Task<List<ChatTurnPerfEvent>> EnumerateBySubjectAsync(string tenantId, string? subjectId, DateTime sinceUtc, CancellationToken token = default);

        /// <summary>Delete all performance events for a subject within a tenant. Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteBySubjectAsync(string tenantId, string subjectId, CancellationToken token = default);

        /// <summary>Delete a subject's performance events older than a cutoff (retention pruning). Idempotent.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="subjectId">Subject identifier.</param>
        /// <param name="cutoffUtc">Events created strictly before this instant are deleted.</param>
        /// <param name="token">Cancellation token.</param>
        Task DeleteOlderThanAsync(string tenantId, string subjectId, DateTime cutoffUtc, CancellationToken token = default);
    }
}
