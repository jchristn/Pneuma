namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;

    /// <summary>
    /// Audit record data access methods.
    /// </summary>
    public interface IAuditMethods
    {
        /// <summary>Insert an audit record.</summary>
        /// <param name="record">Record to insert.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Created record.</returns>
        Task<AuditRecord> CreateAsync(AuditRecord record, CancellationToken token = default);

        /// <summary>Read an audit record by identifier.</summary>
        /// <param name="id">Record identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record, or null if not found.</returns>
        Task<AuditRecord?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>Enumerate audit records for a tenant, most recent first, with a limit.</summary>
        /// <param name="tenantId">Tenant identifier, or null for all tenants (admin).</param>
        /// <param name="maxResults">Maximum records to return.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Records.</returns>
        Task<List<AuditRecord>> EnumerateAsync(string? tenantId, int maxResults, CancellationToken token = default);

        /// <summary>Prune audit records older than the given UTC cutoff.</summary>
        /// <param name="olderThanUtc">UTC cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
