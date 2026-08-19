namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Authentication session data access methods.
    /// </summary>
    public interface IAuthSessionMethods
    {
        /// <summary>Create a session.</summary>
        /// <param name="session">Session to create.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created session.</returns>
        Task<AuthSession> CreateAsync(AuthSession session, CancellationToken token = default);

        /// <summary>Read a session by identifier.</summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Session, or null if not found.</returns>
        Task<AuthSession?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate sessions within a tenant.</summary>
        /// <param name="tenantId">Tenant identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sessions.</returns>
        Task<List<AuthSession>> EnumerateAsync(string tenantId, CancellationToken token = default);

        /// <summary>Update a session.</summary>
        /// <param name="session">Session to update.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Updated session.</returns>
        Task<AuthSession> UpdateAsync(AuthSession session, CancellationToken token = default);

        /// <summary>Delete a session by identifier.</summary>
        /// <param name="id">Session identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>Delete sessions that expired before the given UTC cutoff.</summary>
        /// <param name="olderThanUtc">UTC cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> DeleteExpiredAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
