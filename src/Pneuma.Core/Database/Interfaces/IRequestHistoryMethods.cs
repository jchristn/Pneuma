namespace Pneuma.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Pneuma.Core.Models;
    using Pneuma.Core.Requests;
    using Pneuma.Core.Responses;

    /// <summary>
    /// Request history data access methods.
    /// </summary>
    public interface IRequestHistoryMethods
    {
        /// <summary>Insert a captured request record.</summary>
        /// <param name="entry">Entry to insert.</param>
        /// <param name="token">Cancellation token.</param>
        Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default);

        /// <summary>Read a single entry by identifier. Tenant scope is enforced when tenantId is non-null.</summary>
        /// <param name="tenantId">Tenant identifier, or null for no tenant scoping (admin).</param>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry, or null if not found.</returns>
        Task<RequestHistoryEntry?> ReadAsync(string? tenantId, string id, CancellationToken token = default);

        /// <summary>Page through entries matching a filter. Bodies are omitted from list results.</summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A page of entries.</returns>
        Task<RequestHistoryPage> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>Return bucketed counts and averages for chart rendering.</summary>
        /// <param name="filter">Filter (uses FromUtc, ToUtc, BucketMinutes).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>A time-bucketed summary.</returns>
        Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>Delete a single entry. Tenant scope is enforced when tenantId is non-null.</summary>
        /// <param name="tenantId">Tenant identifier, or null for no tenant scoping (admin).</param>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True if a record was deleted.</returns>
        Task<bool> DeleteAsync(string? tenantId, string id, CancellationToken token = default);

        /// <summary>Delete all entries matching a filter.</summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default);

        /// <summary>Prune entries older than the given UTC cutoff.</summary>
        /// <param name="olderThanUtc">UTC cutoff.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Number of rows deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
